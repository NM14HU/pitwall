// =============================================================
//  PIT WALL · Generador
//
//  1. Noticias: lee los feeds RSS/Atom de feeds.json, clasifica cada
//     noticia en sus secciones y escribe ../web/data/noticias.json
//  2. Mundial: lee clasificaciones y calendario de la API de Jolpica
//     y los trazados de los circuitos, y escribe ../web/data/mundial.json
//  3. Campeonatos: lee F2 y F3 de sus webs oficiales y escribe
//     ../web/data/campeonatos.json
//
//  Uso (desde la carpeta generador):
//     dotnet run                     → genera todo
//     dotnet run -- --comprobar      → solo comprueba los feeds
//     dotnet run -- --solo-noticias  → solo noticias.json
//     dotnet run -- --solo-mundial   → solo mundial.json y campeonatos.json
//     dotnet run -- --depurar        → guarda las páginas de F2/F3 en generador/depuracion
// =============================================================

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Generador;

bool soloComprobar = args.Contains("--comprobar");
bool soloNoticias = args.Contains("--solo-noticias");
bool soloMundial = args.Contains("--solo-mundial");

string carpetaProyecto = Rutas.CarpetaProyecto();
Configuracion config = Configuracion.Cargar(Path.Combine(carpetaProyecto, "feeds.json"));

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
http.DefaultRequestHeaders.UserAgent.ParseAdd(
    "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Safari/605.1.15");
http.DefaultRequestHeaders.Accept.ParseAdd("application/rss+xml, application/atom+xml, application/xml;q=0.9, text/xml;q=0.9, */*;q=0.8");
http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es-ES,es;q=0.9,en;q=0.8");

var opcionesJson = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping   // conserva tildes y eñes legibles
};

if (!soloMundial)
    await GenerarNoticiasAsync();

if (!soloComprobar && !soloNoticias && config.Mundial.Activo)
    await new GeneradorMundial(http, carpetaProyecto, config.Mundial, opcionesJson).GenerarAsync();

if (!soloComprobar && !soloNoticias && config.Campeonatos.Activo)
    await new GeneradorCampeonatos(http, carpetaProyecto, config.Campeonatos.Lista, config.Campeonatos.Salida,
                                   args.Contains("--depurar"), opcionesJson).GenerarAsync();

// -------------------------------------------------------------
//  Noticias
// -------------------------------------------------------------
async Task GenerarNoticiasAsync()
{
    List<FuenteFeed> activos = config.Feeds.Where(f => f.Activo).ToList();

    Console.WriteLine($"Leyendo {activos.Count} feeds...");
    var lector = new LectorFeeds(http);
    ResultadoFeed[] resultados = await Task.WhenAll(activos.Select(lector.LeerAsync));

    Informe.Mostrar(resultados);
    if (soloComprobar) return;

    // 3. Clasificación y filtrado
    var clasificador = new Clasificador(config.Reglas);
    DateTimeOffset limite = DateTimeOffset.Now.AddDays(-config.DiasMaximos);
    var noticias = new List<Noticia>();

    foreach (ResultadoFeed resultado in resultados.Where(r => r.Ok))
    {
        IEnumerable<EntradaFeed> entradas = resultado.Entradas.OrderByDescending(e => e.Fecha);
        if (resultado.Fuente.Maximo > 0) entradas = entradas.Take(resultado.Fuente.Maximo);

        foreach (EntradaFeed entrada in entradas)
        {
            if (entrada.Fecha < limite) continue;
            if (resultado.Fuente.UrlContiene.Length > 0 &&
                !entrada.Url.Contains(resultado.Fuente.UrlContiene, StringComparison.OrdinalIgnoreCase)) continue;

            List<string> etiquetas = clasificador.Clasificar(resultado.Fuente, $"{entrada.Titulo} {entrada.Resumen}");
            if (etiquetas.Count == 0) continue;   // no es de ninguna de nuestras secciones

            noticias.Add(new Noticia
            {
                Titulo = entrada.Titulo,
                Resumen = config.Resumen switch
                {
                    "no" => "",
                    "corto" => LectorFeeds.Recortar(entrada.Resumen, 110),
                    _ => entrada.Resumen
                },
                Medio = config.NombreMedio(entrada.Medio),
                Url = entrada.Url,
                Imagen = !config.Imagenes || string.IsNullOrEmpty(entrada.Imagen) ? Rutas.ImagenSinFoto : entrada.Imagen,
                Fecha = entrada.Fecha,
                Etiquetas = etiquetas,
                Idioma = resultado.Fuente.Idioma
            });
        }
    }

    // Cada sección se queda con sus noticias más recientes (hasta MaxPorSeccion),
    // para que las secciones con menos movimiento no queden tapadas por la F1
    var porSeccion = new Dictionary<string, int>();
    var finales = new List<Noticia>();
    foreach (Noticia n in Duplicados.Quitar(noticias.OrderByDescending(n => n.Fecha)))
    {
        if (!n.Etiquetas.Any(e => porSeccion.GetValueOrDefault(e) < config.MaxPorSeccion)) continue;
        foreach (string e in n.Etiquetas) porSeccion[e] = porSeccion.GetValueOrDefault(e) + 1;
        finales.Add(n);
        if (finales.Count >= config.MaxNoticias) break;
    }

    // Fotos de las noticias cuyo feed no las trae: se buscan en la página del artículo
    if (config.Imagenes && config.BuscarImagenEnArticulo && finales.Count > 0)
    {
        Console.WriteLine();
        Console.Write("Buscando fotos en las páginas de las noticias que no la traen... ");
        var buscador = new ImagenesArticulo(http, Path.Combine(carpetaProyecto, "cache-imagenes.json"));
        int encontradas = await buscador.CompletarAsync(finales);
        Console.WriteLine($"{encontradas} encontradas.");
    }

    // Si no ha llegado nada (sin Internet, todos los feeds caídos...), se conserva el archivo anterior
    if (finales.Count == 0)
    {
        Console.WriteLine();
        Console.WriteLine("No se ha obtenido ninguna noticia: se mantiene el noticias.json anterior.");
        return;
    }

    // 4. Escritura de noticias.json
    string archivoSalida = Path.GetFullPath(Path.Combine(carpetaProyecto, config.Salida));
    Directory.CreateDirectory(Path.GetDirectoryName(archivoSalida)!);
    Rutas.AsegurarImagenSinFoto(archivoSalida);

    var salida = new { actualizado = DateTimeOffset.Now, ejemplo = false, noticias = finales };
    await File.WriteAllTextAsync(archivoSalida, JsonSerializer.Serialize(salida, opcionesJson));

    Console.WriteLine();
    Console.WriteLine($"Guardadas {finales.Count} noticias en {archivoSalida}");

    // Origen de las noticias publicadas: directas de los medios o a través de Google Noticias
    int porGoogle = finales.Count(n => n.Url.Contains("news.google.com", StringComparison.OrdinalIgnoreCase));
    Console.WriteLine($"   Directas de los medios   {finales.Count - porGoogle,4}");
    Console.WriteLine($"   Por Google Noticias      {porGoogle,4}");
    Console.WriteLine("Por secciones:");
    foreach (var grupo in finales.SelectMany(n => n.Etiquetas).GroupBy(e => e).OrderBy(g => Clasificador.Orden(g.Key)))
        Console.WriteLine($"   {grupo.Key,-7} {grupo.Count(),4}");
}
