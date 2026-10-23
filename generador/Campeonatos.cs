using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Generador;

/// <summary>Un campeonato que se lee de su web oficial (F2 y F3 usan el mismo sistema).</summary>
public sealed class ConfigCampeonato
{
    public string Id { get; set; } = "";          // "F2", "F3"…
    public string Nombre { get; set; } = "";      // "Fórmula 2"
    /// <summary>Página de la clasificación de pilotos; {temporada} se sustituye por el año.</summary>
    public string Url { get; set; } = "";

    /// <summary>Página de la clasificación de equipos (opcional).</summary>
    public string UrlEquipos { get; set; } = "";
    public bool Activo { get; set; } = true;
}

/// <summary>
/// Lee la clasificación de pilotos y el calendario de F2 y F3 de sus webs oficiales
/// y crea web/data/campeonatos.json.
/// </summary>
public sealed class GeneradorCampeonatos(HttpClient http, string carpetaProyecto, List<ConfigCampeonato> campeonatos,
                                         string salida, bool depurar, JsonSerializerOptions opcionesJson)
{
    static readonly Regex RegexTabla = new(@"<table\b.*?</table>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex RegexFila = new(@"<tr\b.*?</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex RegexCelda = new(@"<t[hd]\b[^>]*>(.*?)</t[hd]>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex RegexSaltos = new(@"<br\s*/?>|</[a-z0-9]+>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex RegexEnlace = new(@"<a\b[^>]*>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex RegexEtiquetas = new("<[^>]+>", RegexOptions.Compiled);

    // "06 - 08 Mar" o "30 Oct - 01 Nov"
    static readonly Regex RegexFechas = new(@"(\d{1,2})\s*([A-Za-z]{3})?\s*-\s*(\d{1,2})\s*([A-Za-z]{3})", RegexOptions.Compiled);

    public async Task GenerarAsync()
    {
        Console.WriteLine();
        Console.WriteLine("Otros campeonatos:");
        var resultado = new List<object>();

        foreach (ConfigCampeonato c in campeonatos.Where(c => c.Activo))
        {
            try
            {
                Campeonato? datos = null;
                int anio = DateTime.Now.Year;

                // Al empezar el año, la temporada nueva puede no tener aún datos: se prueba la anterior
                foreach (int temporada in new[] { anio, anio - 1 })
                {
                    string html = await http.GetStringAsync(c.Url.Replace("{temporada}", temporada.ToString()));
                    if (depurar) GuardarDepuracion(c.Id, temporada, html);
                    datos = Leer(html, temporada);
                    if (datos.Calendario.Count > 0) break;
                }

                if (datos is null || datos.Calendario.Count == 0)
                    throw new InvalidDataException("no se ha encontrado la tabla de clasificación en la página");

                // Clasificación de equipos: misma tabla, misma temporada. Si falla, se sigue sin ella
                List<Piloto> equipos = [];
                if (c.UrlEquipos.Length > 0)
                {
                    try
                    {
                        string htmlEquipos = await http.GetStringAsync(c.UrlEquipos.Replace("{temporada}", datos.Temporada.ToString()));
                        if (depurar) GuardarDepuracion($"{c.Id}-equipos", datos.Temporada, htmlEquipos);
                        equipos = Leer(htmlEquipos, datos.Temporada).Pilotos;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"   {c.Nombre}: sin clasificación de equipos ({ex.Message})");
                    }
                }

                var proxima = datos.Calendario.FirstOrDefault(r => r.Fin >= DateOnly.FromDateTime(DateTime.Today));
                resultado.Add(new
                {
                    id = c.Id,
                    nombre = c.Nombre,
                    temporada = datos.Temporada,
                    totalRondas = datos.Calendario.Count,
                    proximaRonda = proxima?.Numero,
                    pilotos = datos.Pilotos.Select(p => new { pos = p.Pos, nombre = p.Nombre, puntos = p.Puntos }),
                    equipos = equipos.Select(e => new { pos = e.Pos, nombre = e.Nombre, puntos = e.Puntos }),
                    calendario = datos.Calendario.Select(r => new
                    {
                        ronda = r.Numero,
                        lugar = r.Lugar,
                        pais = Traducir.Pais(r.Pais),
                        inicio = r.Inicio.ToString("yyyy-MM-dd"),
                        fin = r.Fin.ToString("yyyy-MM-dd")
                    })
                });

                string lider = datos.Pilotos.FirstOrDefault() is { } p1 ? $"{p1.Nombre} ({p1.Puntos} pts)" : "-";
                Console.WriteLine($"   {c.Nombre} {datos.Temporada}: {datos.Calendario.Count} rondas, {datos.Pilotos.Count} pilotos, {equipos.Count} equipos, líder {lider}"
                                  + (proxima is null ? ", temporada terminada" : $", próxima ronda {proxima.Numero} ({proxima.Lugar})"));
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"   {c.Nombre}: no se ha podido leer ({ex.Message})");
                Console.ResetColor();
            }
        }

        // Si no se ha podido leer ningún campeonato, se mantiene el archivo anterior
        if (resultado.Count == 0)
        {
            Console.WriteLine("   Se mantiene el campeonatos.json anterior.");
            return;
        }

        string ruta = Path.GetFullPath(Path.Combine(carpetaProyecto, salida));
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        await File.WriteAllTextAsync(ruta, JsonSerializer.Serialize(new { actualizado = DateTimeOffset.Now, campeonatos = resultado }, opcionesJson));
        Console.WriteLine($"   Guardado en {ruta}");
    }

    // ---------- Lectura de la tabla ----------

    sealed record Ronda(int Numero, string Lugar, string Pais, DateOnly Inicio, DateOnly Fin);
    sealed record Piloto(int Pos, string Nombre, double Puntos);
    sealed record Campeonato(int Temporada, List<Ronda> Calendario, List<Piloto> Pilotos);

    static Campeonato Leer(string html, int temporada)
    {
        // La tabla buena es la que tiene la columna "Points"
        string tabla = RegexTabla.Matches(html).Select(m => m.Value)
                                 .FirstOrDefault(t => t.Contains("Points", StringComparison.OrdinalIgnoreCase)) ?? "";

        var calendario = new List<Ronda>();
        var pilotos = new List<Piloto>();

        foreach (Match fila in RegexFila.Matches(tabla))
        {
            List<string> crudas = RegexCelda.Matches(fila.Value).Select(m => m.Groups[1].Value).ToList();
            List<string> celdas = crudas.Select(Texto).ToList();
            if (celdas.Count == 0) continue;

            // Cabecera con las rondas: cada celda trae país, ciudad y fechas en líneas separadas
            if (calendario.Count == 0 && celdas.Count(t => RegexFechas.IsMatch(t)) >= 2)
            {
                for (int i = 0; i < celdas.Count; i++)
                {
                    string celda = celdas[i];
                    Match fechas = RegexFechas.Match(celda);
                    if (!fechas.Success) continue;
                    (DateOnly inicio, DateOnly fin) = Fechas(fechas, temporada);

                    // La ciudad es el enlace de la celda; el país, el resto del texto sin la ciudad ni las fechas
                    // El país es el texto que hay antes del enlace (p. ej. "Barcelona-Catalunya" antes de "Barcelona")
                    Match enlace = RegexEnlace.Match(crudas[i]);
                    string lugar = enlace.Success ? Texto(enlace.Groups[1].Value).Replace('\n', ' ') : "";
                    string antes = enlace.Success ? Texto(crudas[i][..enlace.Index]) : "";
                    string pais = Regex.Replace(antes.Replace(fechas.Value, " ").Replace('\n', ' '), @"\s+", " ").Trim();

                    if (lugar.Length == 0)
                    {
                        // Sin enlace: país y ciudad en líneas separadas
                        string[] lineas = celda.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                               .Where(l => !RegexFechas.IsMatch(l)).ToArray();
                        pais = lineas.ElementAtOrDefault(0) ?? "";
                        lugar = lineas.ElementAtOrDefault(1) ?? pais;
                    }
                    if (pais.Length == 0) pais = lugar;
                    calendario.Add(new Ronda(calendario.Count + 1, lugar, pais, inicio, fin));
                }
                continue;
            }

            // Filas de pilotos: "1R. Câmara" (o "1" y "R. Câmara" en líneas separadas) … puntos al final
            Match primera = Regex.Match(celdas[0].Replace('\n', ' '), @"^\s*(\d+)\s*(.+?)\s*$");
            if (!primera.Success) continue;
            string ultima = celdas[^1].Trim();
            if (!double.TryParse(ultima, NumberStyles.Float, CultureInfo.InvariantCulture, out double puntos)) continue;

            pilotos.Add(new Piloto(int.Parse(primera.Groups[1].Value), primera.Groups[2].Value.Trim(), puntos));
        }

        return new Campeonato(temporada, calendario, pilotos.OrderBy(p => p.Pos).ToList());
    }

    static (DateOnly, DateOnly) Fechas(Match m, int temporada)
    {
        int diaIni = int.Parse(m.Groups[1].Value), diaFin = int.Parse(m.Groups[3].Value);
        int mesFin = Mes(m.Groups[4].Value);
        int mesIni = m.Groups[2].Success && m.Groups[2].Value.Length > 0 ? Mes(m.Groups[2].Value) : mesFin;
        return (new DateOnly(temporada, mesIni, diaIni), new DateOnly(temporada, mesFin, diaFin));
    }

    static int Mes(string abreviatura) =>
        DateTime.ParseExact(abreviatura, "MMM", CultureInfo.InvariantCulture).Month;

    /// <summary>Texto de una celda, conservando los saltos de línea entre bloques.</summary>
    static string Texto(string html)
    {
        string conSaltos = RegexSaltos.Replace(html, "\n");
        string texto = WebUtility.HtmlDecode(RegexEtiquetas.Replace(conSaltos, " "));
        return string.Join('\n', texto.Split('\n').Select(l => Regex.Replace(l, @"\s+", " ").Trim()).Where(l => l.Length > 0));
    }

    /// <summary>Con --depurar, guarda la página descargada para poder revisarla si algo no se lee bien.</summary>
    void GuardarDepuracion(string id, int temporada, string html)
    {
        string carpeta = Path.Combine(carpetaProyecto, "depuracion");
        Directory.CreateDirectory(carpeta);
        File.WriteAllText(Path.Combine(carpeta, $"{id}-{temporada}.html"), html);
    }
}
