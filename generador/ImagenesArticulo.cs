using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Generador;

/// <summary>
/// Para las noticias cuyo feed no trae foto, abre la página del artículo y toma la imagen
/// que el propio medio indica para compartir en redes (og:image). Lo encontrado se guarda
/// en cache-imagenes.json para no volver a descargar la misma página en cada ejecución.
/// </summary>
public sealed class ImagenesArticulo(HttpClient http, string rutaCache)
{
    static readonly Regex RegexMeta = new(
        @"<meta\s[^>]*(?:property|name)\s*=\s*[""'](?:og:image|og:image:url|twitter:image)[""'][^>]*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex RegexContent = new(@"content\s*=\s*[""']([^""']+)[""']", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    const int SimultaneasMax = 6;
    const int BytesMax = 400_000;   // la etiqueta og:image está en la cabecera; no hace falta descargar la página entera

    public async Task<int> CompletarAsync(List<Noticia> noticias)
    {
        Dictionary<string, string> cache = CargarCache();
        var pendientes = noticias.Where(n => n.Imagen == Rutas.ImagenSinFoto && !EsDeGoogle(n.Url)).ToList();
        int encontradas = 0;

        using var semaforo = new SemaphoreSlim(SimultaneasMax);
        await Task.WhenAll(pendientes.Select(async n =>
        {
            string? imagen;
            lock (cache) cache.TryGetValue(n.Url, out imagen);

            if (imagen is null)
            {
                await semaforo.WaitAsync();
                try { imagen = await BuscarAsync(n.Url); }
                finally { semaforo.Release(); }
                if (imagen is not null) lock (cache) cache[n.Url] = imagen;   // "" = la página no tiene imagen
            }

            if (!string.IsNullOrEmpty(imagen))
            {
                n.Imagen = imagen;
                Interlocked.Increment(ref encontradas);
            }
        }));

        // Se guardan solo las noticias que siguen en la web, para que la caché no crezca sin fin
        var vigentes = noticias.Select(n => n.Url).ToHashSet();
        var podada = cache.Where(c => vigentes.Contains(c.Key)).ToDictionary(c => c.Key, c => c.Value);
        await File.WriteAllTextAsync(rutaCache, JsonSerializer.Serialize(podada, new JsonSerializerOptions { WriteIndented = true }));

        return encontradas;
    }

    static bool EsDeGoogle(string url) => url.Contains("news.google.com", StringComparison.OrdinalIgnoreCase);

    /// <summary>Devuelve la imagen, "" si la página no tiene, o null si no se pudo descargar (se reintentará).</summary>
    async Task<string?> BuscarAsync(string url)
    {
        try
        {
            using var cancelacion = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using HttpResponseMessage respuesta = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancelacion.Token);
            if (!respuesta.IsSuccessStatusCode) return null;

            await using Stream flujo = await respuesta.Content.ReadAsStreamAsync(cancelacion.Token);
            var buffer = new byte[BytesMax];
            int leidos = 0, n;
            while (leidos < BytesMax && (n = await flujo.ReadAsync(buffer.AsMemory(leidos, BytesMax - leidos), cancelacion.Token)) > 0)
                leidos += n;

            string html = Encoding.UTF8.GetString(buffer, 0, leidos);
            Match meta = RegexMeta.Match(html);
            if (!meta.Success) return "";

            Match contenido = RegexContent.Match(meta.Value);
            if (!contenido.Success) return "";

            string imagen = WebUtility.HtmlDecode(contenido.Groups[1].Value.Trim());
            return Uri.TryCreate(new Uri(url), imagen, out Uri? absoluta) && absoluta.Scheme.StartsWith("http")
                ? absoluta.ToString()
                : "";
        }
        catch
        {
            return null;
        }
    }

    Dictionary<string, string> CargarCache()
    {
        try
        {
            return File.Exists(rutaCache)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(rutaCache)) ?? []
                : [];
        }
        catch
        {
            return [];
        }
    }
}
