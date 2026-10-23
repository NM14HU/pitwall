using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Generador;

/// <summary>Descarga un feed y lo convierte en una lista de entradas, sea RSS o Atom.</summary>
public sealed class LectorFeeds(HttpClient http)
{
    static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    static readonly XNamespace Media = "http://search.yahoo.com/mrss/";

    static readonly Regex RegexEtiquetas = new("<[^>]+>", RegexOptions.Compiled);
    static readonly Regex RegexEspacios = new(@"\s+", RegexOptions.Compiled);
    static readonly Regex RegexImg = new(@"<img[^>]+src\s*=\s*[""']([^""']+)[""']", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    const int LargoResumen = 220;

    public async Task<ResultadoFeed> LeerAsync(FuenteFeed fuente)
    {
        try
        {
            using HttpResponseMessage respuesta = await http.GetAsync(fuente.Url);
            if (!respuesta.IsSuccessStatusCode)
                return new(fuente, false, $"El servidor respondió {(int)respuesta.StatusCode} {respuesta.ReasonPhrase}", []);

            byte[] bytes = await respuesta.Content.ReadAsByteArrayAsync();
            XDocument documento = CargarXml(bytes);

            List<EntradaFeed> entradas = documento.Root?.Name.LocalName switch
            {
                "rss" or "RDF" => LeerRss(documento, fuente),
                "feed" => LeerAtom(documento, fuente),
                _ => throw new InvalidDataException("La dirección no devuelve un feed RSS ni Atom")
            };
            return new(fuente, true, "OK", entradas);
        }
        catch (TaskCanceledException)
        {
            return new(fuente, false, "No respondió a tiempo (20 s)", []);
        }
        catch (Exception ex)
        {
            return new(fuente, false, ex.Message, []);
        }
    }

    // ---------- Lectura del XML ----------

    static XDocument CargarXml(byte[] bytes)
    {
        // Algunos feeds traen espacios o saltos de línea antes de <?xml ...?>, lo que rompe el lector
        int inicio = 0;
        while (inicio < bytes.Length && bytes[inicio] is (byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t') inicio++;

        var ajustes = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
        using var flujo = new MemoryStream(bytes, inicio, bytes.Length - inicio);
        using var lector = XmlReader.Create(flujo, ajustes);
        return XDocument.Load(lector);
    }

    // RSS 2.0 y RSS 1.0 (RDF): se busca por nombre local para no depender de los espacios de nombres
    static List<EntradaFeed> LeerRss(XDocument documento, FuenteFeed fuente)
    {
        var lista = new List<EntradaFeed>();
        foreach (XElement item in documento.Descendants().Where(e => e.Name.LocalName == "item"))
        {
            string titulo = TextoPlano(Hijo(item, "title")?.Value);
            string url = (Hijo(item, "link")?.Value ?? Hijo(item, "guid")?.Value ?? "").Trim();
            string descripcion = Hijo(item, "description")?.Value ?? "";
            string contenido = Hijo(item, "encoded")?.Value ?? "";
            DateTimeOffset fecha = Fechas.Leer(Hijo(item, "pubDate")?.Value ?? Hijo(item, "date")?.Value);

            string medio = fuente.Medio;
            string resumen = Recortar(TextoPlano(descripcion), LargoResumen);

            if (fuente.MedioDesdeNoticia)
            {
                // Google Noticias: el medio viene en <source> y también al final del título ("Titular - Medio")
                medio = Hijo(item, "source")?.Value.Trim() is { Length: > 0 } m ? m : fuente.Medio;
                if (titulo.EndsWith($" - {medio}")) titulo = titulo[..^($" - {medio}".Length)];
                resumen = "";   // su descripción es una lista de enlaces, no un resumen
            }

            if (titulo.Length == 0 || url.Length == 0) continue;
            lista.Add(new EntradaFeed(titulo, resumen, url, BuscarImagen(item, descripcion + contenido), fecha, medio));
        }
        return lista;
    }

    static List<EntradaFeed> LeerAtom(XDocument documento, FuenteFeed fuente)
    {
        var lista = new List<EntradaFeed>();
        foreach (XElement entrada in documento.Root!.Elements(Atom + "entry"))
        {
            string titulo = TextoPlano(entrada.Element(Atom + "title")?.Value);
            string url = entrada.Elements(Atom + "link")
                                .FirstOrDefault(l => (string?)l.Attribute("rel") is null or "alternate")
                                ?.Attribute("href")?.Value.Trim() ?? "";
            string html = entrada.Element(Atom + "summary")?.Value ?? entrada.Element(Atom + "content")?.Value ?? "";
            DateTimeOffset fecha = Fechas.Leer(entrada.Element(Atom + "published")?.Value ?? entrada.Element(Atom + "updated")?.Value);

            string imagen = entrada.Elements(Atom + "link")
                                   .FirstOrDefault(l => (string?)l.Attribute("rel") == "enclosure"
                                                     && ((string?)l.Attribute("type") ?? "").StartsWith("image"))
                                   ?.Attribute("href")?.Value ?? BuscarImagen(entrada, html);

            if (titulo.Length == 0 || url.Length == 0) continue;
            lista.Add(new EntradaFeed(titulo, Recortar(TextoPlano(html), LargoResumen), url, Absoluta(imagen), fecha, fuente.Medio));
        }
        return lista;
    }

    // ---------- Utilidades ----------

    static XElement? Hijo(XElement padre, string nombreLocal) =>
        padre.Elements().FirstOrDefault(e => e.Name.LocalName == nombreLocal);

    static readonly Regex RegexExtensionImagen = new(@"\.(jpe?g|png|webp|gif|avif)(\?|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Busca la imagen de la noticia en los sitios habituales de los feeds.</summary>
    static string BuscarImagen(XElement item, string html)
    {
        // <media:content> (puede ir dentro de <media:group>): vale salvo que diga que es vídeo o audio
        string? media = item.Descendants(Media + "content")
                            .Where(e => !EsVideoOAudio(e))
                            .Select(e => (string?)e.Attribute("url"))
                            .FirstOrDefault(u => !string.IsNullOrWhiteSpace(u))
                     ?? item.Descendants(Media + "thumbnail").Select(e => (string?)e.Attribute("url")).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(media)) return Absoluta(media);

        // <enclosure>: con tipo de imagen o, si no indica tipo, con extensión de imagen
        string? adjunto = item.Elements()
                              .Where(e => e.Name.LocalName == "enclosure")
                              .Where(e => ((string?)e.Attribute("type") ?? "").StartsWith("image")
                                       || (e.Attribute("type") is null && RegexExtensionImagen.IsMatch((string?)e.Attribute("url") ?? "")))
                              .Select(e => (string?)e.Attribute("url"))
                              .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(adjunto)) return Absoluta(adjunto);

        // <image> o <thumbnail> sueltos que usan algunos medios españoles
        string? suelta = item.Elements()
                             .Where(e => e.Name.LocalName is "image" or "thumbnail" or "imagen")
                             .Select(e => (string?)e.Attribute("url") ?? (string?)e.Attribute("href") ?? Hijo(e, "url")?.Value ?? e.Value)
                             .FirstOrDefault(u => !string.IsNullOrWhiteSpace(u) && u.Trim().StartsWith("http"));
        if (!string.IsNullOrWhiteSpace(suelta)) return Absoluta(suelta);

        // Primera <img> dentro del texto de la noticia
        Match img = RegexImg.Match(html);
        return img.Success ? Absoluta(WebUtility.HtmlDecode(img.Groups[1].Value)) : "";
    }

    static bool EsVideoOAudio(XElement e)
    {
        string medium = (string?)e.Attribute("medium") ?? "";
        string tipo = (string?)e.Attribute("type") ?? "";
        return medium is "video" or "audio" || tipo.StartsWith("video") || tipo.StartsWith("audio");
    }

    static string Absoluta(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        url = url.Trim();
        if (url.StartsWith("//")) return "https:" + url;
        return url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : "";
    }

    /// <summary>Quita las etiquetas HTML y convierte &amp;amp; y similares en texto normal.</summary>
    public static string TextoPlano(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        string texto = WebUtility.HtmlDecode(RegexEtiquetas.Replace(html, " "));
        texto = WebUtility.HtmlDecode(texto);   // algunos feeds codifican dos veces (&amp;quot;)
        return RegexEspacios.Replace(texto, " ").Trim();
    }

    public static string Recortar(string texto, int maximo)
    {
        if (texto.Length <= maximo) return texto;
        int corte = texto.LastIndexOf(' ', maximo);
        return texto[..(corte > maximo / 2 ? corte : maximo)].TrimEnd(',', ';', ':', '.') + "…";
    }
}

/// <summary>Las fechas de los feeds vienen en formatos muy variados; aquí se interpretan todos.</summary>
public static class Fechas
{
    static readonly (string Zona, string Desfase)[] Zonas =
    [
        ("GMT", "+00:00"), ("UTC", "+00:00"), ("UT", "+00:00"), ("Z", "+00:00"),
        ("CEST", "+02:00"), ("CET", "+01:00"), ("BST", "+01:00"),
        ("EDT", "-04:00"), ("EST", "-05:00"), ("PDT", "-07:00"), ("PST", "-08:00")
    ];

    static readonly string[] Formatos =
    [
        "d MMM yyyy HH:mm:ss zzz", "d MMM yyyy HH:mm zzz",
        "d MMMM yyyy HH:mm:ss zzz", "yyyy-MM-dd HH:mm:ss zzz"
    ];

    public static DateTimeOffset Leer(string? texto)
    {
        DateTimeOffset ahora = DateTimeOffset.Now;
        if (string.IsNullOrWhiteSpace(texto)) return ahora;
        texto = texto.Trim();

        if (!DateTimeOffset.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset fecha))
        {
            string limpio = Regex.Replace(texto, @"^[^\d,]+,\s*", "");          // quita el día de la semana
            foreach (var (zona, desfase) in Zonas)
                limpio = Regex.Replace(limpio, $@"\s{zona}$", " " + desfase);
            limpio = Regex.Replace(limpio, @"([+-]\d{2})(\d{2})$", "$1:$2");     // +0200 → +02:00

            bool leida = false;
            foreach (var cultura in new[] { CultureInfo.InvariantCulture, new CultureInfo("es-ES") })
            {
                if (DateTimeOffset.TryParseExact(limpio, Formatos, cultura, DateTimeStyles.AllowWhiteSpaces, out fecha) ||
                    DateTimeOffset.TryParse(limpio, cultura, DateTimeStyles.AssumeUniversal, out fecha))
                {
                    leida = true;
                    break;
                }
            }
            if (!leida) return ahora;
        }

        // Una fecha en el futuro suele ser un error del feed
        return fecha > ahora.AddHours(1) ? ahora : fecha;
    }
}
