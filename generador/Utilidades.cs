using System.Globalization;
using System.Text;

namespace Generador;

/// <summary>Quita noticias repetidas: misma dirección o titulares casi iguales de distintos medios.</summary>
public static class Duplicados
{
    const double Parecido = 0.6;   // 60 % de palabras en común

    public static IEnumerable<Noticia> Quitar(IEnumerable<Noticia> ordenadas)
    {
        var porUrl = new Dictionary<string, Noticia>();
        var porTitular = new Dictionary<string, Noticia>();
        var titulares = new List<(HashSet<string> Palabras, Noticia Noticia)>();

        foreach (Noticia n in ordenadas)
        {
            // Misma dirección: es la misma noticia
            if (porUrl.TryGetValue(NormalizarUrl(n.Url), out Noticia? igual))
            {
                CompletarFoto(igual, n);
                continue;
            }

            // Mismo titular (sin contar mayúsculas, tildes ni signos), aunque sea muy corto
            string titularNormal = string.Join(" ", Palabras(n.Titulo, minimo: 1).Order());
            if (titularNormal.Length > 0 && porTitular.TryGetValue(titularNormal, out Noticia? mismoTitular))
            {
                CompletarFoto(mismoTitular, n);
                continue;
            }

            // Titular casi igual: la misma noticia contada por otro medio
            HashSet<string> palabras = Palabras(n.Titulo);
            var parecida = palabras.Count >= 4
                ? titulares.FirstOrDefault(t => Jaccard(t.Palabras, palabras) >= Parecido
                                              || Solapamiento(t.Palabras, palabras) >= 0.7).Noticia
                : null;
            if (parecida is not null)
            {
                CompletarFoto(parecida, n);
                continue;
            }

            porUrl[NormalizarUrl(n.Url)] = n;
            if (titularNormal.Length > 0) porTitular[titularNormal] = n;
            titulares.Add((palabras, n));
            yield return n;
        }
    }

    /// <summary>Si la noticia que se queda no tiene foto y su duplicado sí, se aprovecha la del duplicado.</summary>
    static void CompletarFoto(Noticia queda, Noticia duplicada)
    {
        if (queda.Imagen == Rutas.ImagenSinFoto && duplicada.Imagen != Rutas.ImagenSinFoto)
            queda.Imagen = duplicada.Imagen;
    }

    static string NormalizarUrl(string url)
    {
        int interrogacion = url.IndexOf('?');
        if (interrogacion > 0 && url.Contains("utm_")) url = url[..interrogacion];
        return url.Trim().TrimEnd('/').ToLowerInvariant();
    }

    /// <summary>Palabras significativas del titular, en minúsculas y sin tildes.</summary>
    static HashSet<string> Palabras(string titulo, int minimo = 4)
    {
        string sinTildes = new string(titulo.ToLowerInvariant()
            .Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray());
        return sinTildes.Split(sinTildes.Where(c => !char.IsLetterOrDigit(c)).Distinct().ToArray(),
                               StringSplitOptions.RemoveEmptyEntries)
                        .Where(p => p.Length >= minimo)
                        .ToHashSet();
    }

    /// <summary>Qué parte del titular más corto está contenida en el otro.</summary>
    static double Solapamiento(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count < 4 || b.Count < 4) return 0;
        return a.Count(b.Contains) / (double)Math.Min(a.Count, b.Count);
    }

    static double Jaccard(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count < 4) return 0;
        int comunes = a.Count(b.Contains);
        return comunes / (double)(a.Count + b.Count - comunes);
    }
}

/// <summary>Muestra en el Terminal cómo ha ido cada feed.</summary>
public static class Informe
{
    public static void Mostrar(IEnumerable<ResultadoFeed> resultados)
    {
        Console.WriteLine();
        Console.WriteLine($"{"Estado",-6}  {"Noticias",8}  {"Con foto",8}  {"Más reciente",-17}  Medio");
        Console.WriteLine(new string('─', 88));

        foreach (ResultadoFeed r in resultados)
        {
            Console.ForegroundColor = r.Ok ? (r.Entradas.Count > 0 ? ConsoleColor.Green : ConsoleColor.Yellow) : ConsoleColor.Red;
            Console.Write($"{(r.Ok ? "OK" : "FALLO"),-6}");
            Console.ResetColor();

            string reciente = r.Entradas.Count > 0
                ? r.Entradas.Max(e => e.Fecha).ToLocalTime().ToString("dd/MM/yyyy HH:mm")
                : "-";
            int conFoto = r.Entradas.Count(e => e.Imagen.Length > 0);
            Console.WriteLine($"  {r.Entradas.Count,8}  {conFoto,8}  {reciente,-17}  {r.Fuente.Medio} ({string.Join(", ", r.Fuente.Secciones)})");

            if (!r.Ok)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"        {r.Mensaje}");
                Console.WriteLine($"        {r.Fuente.Url}");
                Console.ResetColor();
            }
        }

        int correctos = resultados.Count(r => r.Ok);
        Console.WriteLine(new string('─', 88));
        Console.WriteLine($"{correctos} de {resultados.Count()} feeds funcionan.");
    }
}

/// <summary>Rutas del proyecto y la imagen que se usa cuando una noticia no trae foto.</summary>
public static class Rutas
{
    public const string ImagenSinFoto = "img/sin-foto.svg";

    /// <summary>Carpeta donde está Generador.csproj, se ejecute desde donde se ejecute.</summary>
    public static string CarpetaProyecto()
    {
        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
        while (carpeta is not null && carpeta.GetFiles("*.csproj").Length == 0)
            carpeta = carpeta.Parent;
        return carpeta?.FullName ?? Directory.GetCurrentDirectory();
    }

    /// <summary>Crea web/img/sin-foto.svg si todavía no existe.</summary>
    public static void AsegurarImagenSinFoto(string archivoNoticias)
    {
        string carpetaWeb = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(archivoNoticias)!, ".."));
        string ruta = Path.Combine(carpetaWeb, ImagenSinFoto);
        if (File.Exists(ruta)) return;

        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        File.WriteAllText(ruta, """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 800 450">
              <rect width="800" height="450" fill="#C9D6DF"/>
              <g fill="#AFC0CC" transform="translate(300 175) skewX(-18)">
                <rect x="0" y="0" width="40" height="100"/>
                <rect x="70" y="0" width="40" height="100"/>
                <rect x="140" y="0" width="40" height="100"/>
              </g>
            </svg>
            """);
    }
}
