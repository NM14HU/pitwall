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

    /// <summary>Formato de la web: vacío para F2/F3 (sus webs oficiales); "fia" para las páginas de la FIA (Fórmula E).</summary>
    public string Tipo { get; set; } = "";
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
        string ruta = Path.GetFullPath(Path.Combine(carpetaProyecto, salida));
        Dictionary<string, JsonElement> anteriores = LeerAnteriores(ruta);
        int leidos = 0;

        foreach (ConfigCampeonato c in campeonatos.Where(c => c.Activo))
        {
            try
            {
                Leido datos = c.Tipo == "fia" ? await LeerFiaAsync(c) : await LeerF2F3Async(c);
                var proxima = datos.Calendario.FirstOrDefault(r => r.Fin >= DateOnly.FromDateTime(DateTime.Today));
                resultado.Add(new
                {
                    id = c.Id,
                    nombre = c.Nombre,
                    actualizado = DateTimeOffset.Now,
                    temporada = datos.Temporada,
                    notaClasificacion = datos.Nota,
                    totalRondas = datos.Calendario.Count,
                    proximaRonda = proxima?.Numero,
                    pilotos = datos.Pilotos.Select(p => new { pos = p.Pos, nombre = p.Nombre, puntos = p.Puntos }),
                    equipos = datos.Equipos.Select(e => new { pos = e.Pos, nombre = e.Nombre, puntos = e.Puntos }),
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
                Console.WriteLine($"   {c.Nombre} {datos.Temporada}: {datos.Calendario.Count} rondas, {datos.Pilotos.Count} pilotos, {datos.Equipos.Count} equipos, líder {lider}"
                                  + (datos.Nota is null ? "" : $" ({datos.Nota})")
                                  + (proxima is null ? ", temporada terminada" : $", próxima ronda {proxima.Numero} ({proxima.Lugar})"));
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"   {c.Nombre}: no se ha podido leer ({ex.Message})");
                Console.ResetColor();

                // Se conservan los últimos datos buenos de ese campeonato, con su fecha
                if (anteriores.TryGetValue(c.Id, out JsonElement anterior))
                {
                    resultado.Add(anterior);
                    Console.WriteLine($"   {c.Nombre}: se mantienen los datos anteriores");
                }
                continue;
            }
            leidos++;
        }

        // Si no se ha podido leer ningún campeonato, el archivo se deja tal cual
        if (leidos == 0)
        {
            Console.WriteLine("   Se mantiene el campeonatos.json anterior.");
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        await File.WriteAllTextAsync(ruta, JsonSerializer.Serialize(new { actualizado = DateTimeOffset.Now, campeonatos = resultado }, opcionesJson));
        Console.WriteLine($"   Guardado en {ruta}");
    }


    /// <summary>Campeonatos del archivo anterior, por id, para conservarlos si esta vez fallan.</summary>
    static Dictionary<string, JsonElement> LeerAnteriores(string ruta)
    {
        var anteriores = new Dictionary<string, JsonElement>();
        try
        {
            if (!File.Exists(ruta)) return anteriores;
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(ruta));
            foreach (JsonElement c in doc.RootElement.GetProperty("campeonatos").EnumerateArray())
                if (c.TryGetProperty("id", out JsonElement id) && id.GetString() is string clave)
                    anteriores[clave] = c.Clone();
        }
        catch { }
        return anteriores;
    }

    // ---------- F2 y F3: sus webs oficiales ----------

    sealed record Leido(string Temporada, List<Ronda> Calendario, List<Piloto> Pilotos, List<Piloto> Equipos, string? Nota);

    async Task<Leido> LeerF2F3Async(ConfigCampeonato c)
    {
        Campeonato? datos = null;
        int anio = DateTime.Now.Year;

        // Al empezar el año, la temporada nueva puede no tener aún datos: se prueba la anterior
        foreach (int temporada in new[] { anio, anio - 1 })
        {
            string html = await http.GetStringAsync(c.Url.Replace("{temporada}", temporada.ToString()));
            if (depurar) GuardarDepuracion(c.Id, temporada.ToString(), html);
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
                if (depurar) GuardarDepuracion($"{c.Id}-equipos", datos.Temporada.ToString(), htmlEquipos);
                equipos = Leer(htmlEquipos, datos.Temporada).Pilotos;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   {c.Nombre}: sin clasificación de equipos ({ex.Message})");
            }
        }
        return new Leido(datos.Temporada.ToString(), datos.Calendario, datos.Pilotos, equipos, null);
    }

    // ---------- Fórmula E: páginas de la FIA ----------

    // Temporadas que cruzan el año ("2025-2026"): de agosto en adelante ya cuenta la siguiente
    static string[] TemporadasFia()
    {
        int a = DateTime.Now.Year;
        return DateTime.Now.Month >= 8
            ? [$"{a}-{a + 1}", $"{a - 1}-{a}"]
            : [$"{a - 1}-{a}", $"{a - 2}-{a - 1}"];
    }

    static string Corta(string temporada) => temporada.Length == 9 ? $"{temporada[..4]}-{temporada[7..]}" : temporada;

    // La web de la FIA es lenta: sus páginas tienen su propio cliente, con más tiempo de espera
    static readonly HttpClient httpFia = CrearClienteFia();

    static HttpClient CrearClienteFia()
    {
        var cliente = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        cliente.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Safari/605.1.15");
        cliente.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml;q=0.9,*/*;q=0.8");
        cliente.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-GB,en;q=0.9,es;q=0.8");
        return cliente;
    }

    static async Task<string?> PedirOpcionalAsync(string url)
    {
        var reloj = System.Diagnostics.Stopwatch.StartNew();
        using var r = await httpFia.GetAsync(url);
        string? texto = r.IsSuccessStatusCode ? await r.Content.ReadAsStringAsync() : null;
        Console.WriteLine($"      FIA: {(int)r.StatusCode} en {reloj.Elapsed.TotalSeconds:0.0} s ← {url[(url.IndexOf("season-") is int i and >= 0 ? i : 0)..]}");
        return texto;
    }

    async Task<Leido> LeerFiaAsync(ConfigCampeonato c)
    {
        string[] temporadas = TemporadasFia();
        List<Ronda>? calendario = null;
        string? temporadaCalendario = null;

        // Calendario: el de la temporada más reciente que la FIA ya tenga publicada
        foreach (string t in temporadas)
        {
            string? html = await PedirOpcionalAsync(c.Url.Replace("{temporada}", t));
            if (html is null) continue;
            if (depurar) GuardarDepuracion($"{c.Id}-calendario", t, html);
            var lista = LeerCalendarioFia(html, t);
            if (lista.Count > 0) { calendario = lista; temporadaCalendario = t; break; }
        }
        if (calendario is null || temporadaCalendario is null)
            throw new InvalidDataException("no se ha encontrado el calendario en la web de la FIA");

        // Clasificación: la de esa temporada; si aún no tiene puntos, la final de la anterior
        List<Piloto> pilotos = [], equipos = [];
        string? nota = null;
        foreach (string t in temporadas.SkipWhile(t => t != temporadaCalendario))
        {
            string? html = await PedirOpcionalAsync(c.UrlEquipos.Replace("{temporada}", t));
            if (html is null) continue;
            if (depurar) GuardarDepuracion($"{c.Id}-clasificacion", t, html);
            (pilotos, equipos) = LeerClasificacionFia(html);
            if (pilotos.Any(p => p.Puntos > 0))
            {
                if (t != temporadaCalendario) nota = $"Clasificación final {Corta(t)}";
                break;
            }
        }
        return new Leido(Corta(temporadaCalendario), calendario, pilotos, equipos, nota);
    }

    // Cada carrera del calendario es un enlace a .../season-AAAA-AAAA/nombre-e-prix, con sus fechas justo antes
    static readonly Regex RegexEventoFia = new(@"<a\b[^>]*href\s*=\s*""[^""]*/championship/events/[^""]*/season-[^""/]+/([^""/]+)""[^>]*>(.*?)</a>",
                                               RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex RegexFechaFia = new(@"(?<![\d])(\d{1,2})(?:\s|<[^>]+>)*(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\b",
                                              RegexOptions.Compiled);
    static readonly Regex RegexBanderaFia = new(@"flat-large/([a-z]{2})\.png", RegexOptions.Compiled);

    static List<Ronda> LeerCalendarioFia(string html, string temporada)
    {
        int anioInicio = int.Parse(temporada[..4]);
        var rondas = new List<Ronda>();
        var vistos = new HashSet<string>();
        int desde = 0;
        foreach (Match m in RegexEventoFia.Matches(html))
        {
            string slug = m.Groups[1].Value;
            if (!slug.Contains("prix", StringComparison.OrdinalIgnoreCase) || !vistos.Add(slug)) { desde = m.Index + m.Length; continue; }

            // Fechas justo antes del enlace: la última y, si es un fin de semana doble, la del día anterior.
            // La temporada va de otoño a verano: de octubre a diciembre es el primer año, el resto el segundo
            var candidatas = RegexFechaFia.Matches(html[Math.Max(desde, m.Index - 1500)..m.Index])
                .Select(f =>
                {
                    int mes = DateTime.ParseExact(f.Groups[2].Value, "MMM", CultureInfo.InvariantCulture).Month;
                    return new DateOnly(mes >= 10 ? anioInicio : anioInicio + 1, mes, int.Parse(f.Groups[1].Value));
                }).ToList();
            var fechas = new List<DateOnly>();
            if (candidatas.Count > 0)
            {
                DateOnly ultima = candidatas[^1];
                fechas = candidatas.Where(f => f <= ultima && ultima.DayNumber - f.DayNumber <= 3).ToList();
            }

            // País: la bandera que va después del enlace
            Match bandera = RegexBanderaFia.Match(html, m.Index);
            string pais = bandera.Success ? PaisIso(bandera.Groups[1].Value) : "";

            string nombre = Texto(m.Groups[2].Value).Split('\n')[0];
            string lugar = Regex.Replace(nombre, @"\s*E-?Prix.*$", "", RegexOptions.IgnoreCase).Trim();
            lugar = CiudadesFe.GetValueOrDefault(lugar, lugar);

            if (fechas.Count > 0)
                rondas.Add(new Ronda(0, lugar, pais, fechas.Min(), fechas.Max()));
            desde = m.Index + m.Length;
        }
        // Por orden de fecha y numeradas
        return rondas.OrderBy(r => r.Inicio).Select((r, i) => r with { Numero = i + 1 }).ToList();
    }

    static (List<Piloto>, List<Piloto>) LeerClasificacionFia(string html)
    {
        var pilotos = new List<Piloto>();
        var equipos = new List<Piloto>();
        foreach (Match t in RegexTabla.Matches(html))
        {
            string tabla = t.Value;
            bool dePilotos = Regex.IsMatch(tabla, @"-\s*Drivers", RegexOptions.IgnoreCase);
            bool deEquipos = Regex.IsMatch(tabla, @"-\s*Teams", RegexOptions.IgnoreCase);
            if (!dePilotos && !deEquipos) continue;

            var lista = dePilotos ? pilotos : equipos;
            if (lista.Count > 0) continue;
            foreach (Match fila in RegexFila.Matches(tabla))
            {
                List<string> celdas = RegexCelda.Matches(fila.Value).Select(m => Texto(m.Groups[1].Value)).ToList();
                if (celdas.Count < 3 || !int.TryParse(celdas[0].Trim(), out int pos)) continue;
                Match puntos = Regex.Match(celdas[^1], @"(\d+(?:\.\d+)?)\s*pts", RegexOptions.IgnoreCase);
                if (!puntos.Success) continue;
                string nombre = celdas[1].Split('\n')[0].Trim();
                lista.Add(new Piloto(pos, dePilotos ? NombreCorto(nombre) : nombre,
                                     double.Parse(puntos.Groups[1].Value, CultureInfo.InvariantCulture)));
            }
        }
        return (pilotos.OrderBy(p => p.Pos).ToList(), equipos.OrderBy(e => e.Pos).ToList());
    }

    // "Oliver ROWLAND" → "O. Rowland"; "António Félix DA COSTA" → "A. Da Costa" (como en F2 y F3)
    static string NombreCorto(string nombre)
    {
        string[] partes = nombre.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var apellido = partes.Where(p => p.Length > 1 && p == p.ToUpperInvariant()).ToList();
        var nombres = partes.Where(p => !apellido.Contains(p)).ToList();
        if (apellido.Count == 0 || nombres.Count == 0) return nombre;
        var ti = CultureInfo.GetCultureInfo("es-ES").TextInfo;
        return $"{nombres[0][0]}. {ti.ToTitleCase(string.Join(' ', apellido).ToLowerInvariant())}";
    }

    static readonly Dictionary<string, string> CiudadesFe = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Mexico City"] = "Ciudad de México", ["London"] = "Londres", ["Berlin"] = "Berlín", ["Tokyo"] = "Tokio",
        ["Monaco"] = "Mónaco", ["Jeddah"] = "Yeda", ["Shanghai"] = "Shanghái", ["Sao Paulo"] = "São Paulo",
        ["Diriyah"] = "Diriyah", ["Rome"] = "Roma", ["Cape Town"] = "Ciudad del Cabo", ["Seoul"] = "Seúl"
    };

    static string PaisIso(string iso) => iso switch
    {
        "br" => "Brasil", "mx" => "México", "us" => "Estados Unidos", "sa" => "Arabia Saudí", "es" => "España",
        "de" => "Alemania", "mc" => "Mónaco", "cn" => "China", "jp" => "Japón", "gb" => "Reino Unido",
        "it" => "Italia", "id" => "Indonesia", "in" => "India", "kr" => "Corea del Sur", "za" => "Sudáfrica",
        "fr" => "Francia", "ch" => "Suiza", "ar" => "Argentina", "cl" => "Chile", "ae" => "Emiratos Árabes Unidos",
        "ma" => "Marruecos", "pt" => "Portugal", "ca" => "Canadá", "qa" => "Catar", "hk" => "Hong Kong",
        "at" => "Austria", "nl" => "Países Bajos", "be" => "Bélgica", _ => iso.ToUpperInvariant()
    };

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
    void GuardarDepuracion(string id, string temporada, string html)
    {
        string carpeta = Path.Combine(carpetaProyecto, "depuracion");
        Directory.CreateDirectory(carpeta);
        File.WriteAllText(Path.Combine(carpeta, $"{id}-{temporada}.html"), html);
    }
}
