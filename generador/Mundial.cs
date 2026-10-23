using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Generador;

/// <summary>Configuración de la parte del Mundial, dentro de feeds.json.</summary>
public sealed class ConfigMundial
{
    public bool Activo { get; set; } = true;
    public string Salida { get; set; } = "../web/data/mundial.json";
    public string Api { get; set; } = "https://api.jolpi.ca/ergast/f1";
    public string Circuitos { get; set; } = "https://raw.githubusercontent.com/bacinger/f1-circuits/master/f1-circuits.geojson";
    public Dictionary<string, string> ColoresEquipos { get; set; } = [];
}

/// <summary>
/// Crea mundial.json con la clasificación de pilotos y equipos, el calendario con los ganadores
/// y la próxima carrera con sus horarios y el trazado del circuito.
/// </summary>
public sealed class GeneradorMundial(HttpClient http, string carpetaProyecto, ConfigMundial config, JsonSerializerOptions opcionesJson)
{
    const string ColorPorDefecto = "#8A8F98";

    public async Task GenerarAsync()
    {
        Console.WriteLine();
        Console.WriteLine("Mundial de F1:");
        try
        {
            // Peticiones una a una: la API pide no hacer muchas a la vez
            JsonElement calendario = await PedirAsync("current.json");
            JsonElement pilotos = await PedirAsync("current/driverstandings.json");
            JsonElement equipos = await PedirAsync("current/constructorstandings.json");
            JsonElement ganadores = await PedirAsync("current/results/1.json?limit=100");
            JsonElement ultima = await PedirAsync("current/last/results.json?limit=100");

            var carreras = LeerCarreras(calendario);
            var proxima = carreras.FirstOrDefault(c => c.Fecha.AddHours(3) > DateTimeOffset.Now);
            var nombresGanadores = LeerGanadores(ganadores);
            var posicionesUltima = LeerUltimaCarrera(ultima);

            object? proximaJson = null;
            string? trazado = null;
            if (proxima is not null)
            {
                string? meta;
                (trazado, meta) = await TrazadoAsync(proxima.Latitud, proxima.Longitud);
                proximaJson = new
                {
                    ronda = proxima.Ronda,
                    nombre = Traducir.Carrera(proxima.NombreOriginal),
                    circuito = proxima.Circuito,
                    pais = Traducir.Pais(proxima.Pais),
                    fecha = proxima.Fecha,
                    sesiones = proxima.Sesiones.OrderBy(s => s.Fecha).Select(s => new { nombre = s.Nombre, fecha = s.Fecha }),
                    trazado,
                    meta
                };
            }

            var mundial = new
            {
                actualizado = DateTimeOffset.Now,
                ejemplo = false,
                temporada = carreras.FirstOrDefault()?.Temporada ?? DateTime.Now.Year.ToString(),
                totalRondas = carreras.Count,
                proximaCarrera = proximaJson,
                pilotos = LeerPilotos(pilotos, posicionesUltima),
                constructores = LeerEquipos(equipos),
                calendario = carreras.Select(c => new
                {
                    ronda = c.Ronda,
                    nombre = Traducir.Carrera(c.NombreOriginal),
                    fecha = c.Fecha,
                    ganador = nombresGanadores.GetValueOrDefault(c.Ronda)
                })
            };

            string salida = Path.GetFullPath(Path.Combine(carpetaProyecto, config.Salida));
            Directory.CreateDirectory(Path.GetDirectoryName(salida)!);
            await File.WriteAllTextAsync(salida, JsonSerializer.Serialize(mundial, opcionesJson));

            Console.WriteLine($"   Temporada {mundial.temporada}: {carreras.Count} carreras, {nombresGanadores.Count} disputadas.");
            Console.WriteLine(proxima is null
                ? "   No quedan carreras esta temporada."
                : $"   Próxima: {Traducir.Carrera(proxima.NombreOriginal)} ({proxima.Fecha.ToLocalTime():dd/MM/yyyy HH:mm})"
                  + (trazado is null ? " (sin trazado del circuito)" : ""));
            Console.WriteLine($"   Guardado en {salida}");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"   No se ha podido actualizar el Mundial: {ex.Message}");
            Console.ResetColor();
            Console.WriteLine("   Se mantiene el mundial.json anterior.");
        }
    }

    // ---------- API de Jolpica ----------

    async Task<JsonElement> PedirAsync(string ruta)
    {
        using HttpResponseMessage respuesta = await http.GetAsync($"{config.Api.TrimEnd('/')}/{ruta}");
        respuesta.EnsureSuccessStatusCode();
        using JsonDocument doc = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        await Task.Delay(300);   // pequeña pausa entre peticiones
        return doc.RootElement.GetProperty("MRData").Clone();
    }

    sealed record Sesion(string Nombre, DateTimeOffset Fecha);
    sealed record Carrera(string Temporada, int Ronda, string NombreOriginal, string Circuito, string Pais,
                          double Latitud, double Longitud, DateTimeOffset Fecha, List<Sesion> Sesiones);

    static readonly (string Clave, string Nombre)[] Sesiones =
    [
        ("FirstPractice", "Libres 1"), ("SecondPractice", "Libres 2"), ("ThirdPractice", "Libres 3"),
        ("SprintQualifying", "Clasificación sprint"), ("SprintShootout", "Clasificación sprint"),
        ("Sprint", "Sprint"), ("Qualifying", "Clasificación")
    ];

    static List<Carrera> LeerCarreras(JsonElement datos)
    {
        var lista = new List<Carrera>();
        foreach (JsonElement c in datos.GetProperty("RaceTable").GetProperty("Races").EnumerateArray())
        {
            JsonElement circuito = c.GetProperty("Circuit");
            JsonElement lugar = circuito.GetProperty("Location");
            DateTimeOffset fecha = Fecha(c);

            var sesiones = new List<Sesion>();
            foreach (var (clave, nombre) in Sesiones)
                if (c.TryGetProperty(clave, out JsonElement s))
                    sesiones.Add(new Sesion(nombre, Fecha(s)));
            sesiones.Add(new Sesion("Carrera", fecha));

            lista.Add(new Carrera(
                Texto(c, "season"), int.Parse(Texto(c, "round")), Texto(c, "raceName"),
                Texto(circuito, "circuitName"), Texto(lugar, "country"),
                Numero(lugar, "lat"), Numero(lugar, "long"), fecha, sesiones));
        }
        return lista.OrderBy(c => c.Ronda).ToList();
    }

    static Dictionary<int, string> LeerGanadores(JsonElement datos)
    {
        var ganadores = new Dictionary<int, string>();
        foreach (JsonElement c in datos.GetProperty("RaceTable").GetProperty("Races").EnumerateArray())
        {
            JsonElement resultados = c.GetProperty("Results");
            if (resultados.GetArrayLength() > 0)
                ganadores[int.Parse(Texto(c, "round"))] = Texto(resultados[0].GetProperty("Driver"), "familyName");
        }
        return ganadores;
    }

    /// <summary>Puesto de cada piloto (por su código, p. ej. "ALO") en la última carrera.</summary>
    static Dictionary<string, string> LeerUltimaCarrera(JsonElement datos)
    {
        var posiciones = new Dictionary<string, string>();
        foreach (JsonElement c in datos.GetProperty("RaceTable").GetProperty("Races").EnumerateArray())
            foreach (JsonElement r in c.GetProperty("Results").EnumerateArray())
            {
                string texto = Texto(r, "positionText");
                posiciones[Texto(r.GetProperty("Driver"), "code")] =
                    int.TryParse(texto, out int puesto) ? $"{puesto}º" : "Abandono";
            }
        return posiciones;
    }

    List<object> LeerPilotos(JsonElement datos, Dictionary<string, string> ultima)
    {
        var lista = new List<object>();
        foreach (JsonElement p in Clasificacion(datos, "DriverStandings"))
        {
            JsonElement piloto = p.GetProperty("Driver");
            JsonElement equipo = p.GetProperty("Constructors").EnumerateArray().LastOrDefault();
            string codigo = Texto(piloto, "code");
            lista.Add(new
            {
                pos = Posicion(p, lista.Count),
                nombre = $"{Texto(piloto, "givenName")} {Texto(piloto, "familyName")}",
                codigo,
                equipo = equipo.ValueKind == JsonValueKind.Object ? Texto(equipo, "name") : "",
                color = equipo.ValueKind == JsonValueKind.Object ? Color(Texto(equipo, "constructorId")) : ColorPorDefecto,
                puntos = Numero(p, "points"),
                ultima = ultima.GetValueOrDefault(codigo)
            });
        }
        return lista;
    }

    List<object> LeerEquipos(JsonElement datos)
    {
        var lista = new List<object>();
        foreach (JsonElement e in Clasificacion(datos, "ConstructorStandings"))
        {
            JsonElement equipo = e.GetProperty("Constructor");
            lista.Add(new
            {
                pos = Posicion(e, lista.Count),
                nombre = Texto(equipo, "name"),
                color = Color(Texto(equipo, "constructorId")),
                puntos = Numero(e, "points")
            });
        }
        return lista;
    }

    static IEnumerable<JsonElement> Clasificacion(JsonElement datos, string tipo)
    {
        JsonElement listas = datos.GetProperty("StandingsTable").GetProperty("StandingsLists");
        return listas.GetArrayLength() == 0 ? [] : listas[0].GetProperty(tipo).EnumerateArray();
    }

    string Color(string idEquipo) => config.ColoresEquipos.GetValueOrDefault(idEquipo, ColorPorDefecto);

    static int Posicion(JsonElement e, int indice) =>
        int.TryParse(Texto(e, "position"), out int p) ? p : indice + 1;

    static string Texto(JsonElement e, string propiedad) =>
        e.TryGetProperty(propiedad, out JsonElement v) ? v.GetString() ?? "" : "";

    static double Numero(JsonElement e, string propiedad) =>
        double.TryParse(Texto(e, propiedad), NumberStyles.Float, CultureInfo.InvariantCulture, out double n) ? n : 0;

    /// <summary>La API da la fecha y la hora por separado ("2026-11-08" y "14:00:00Z").</summary>
    static DateTimeOffset Fecha(JsonElement e)
    {
        string fecha = Texto(e, "date");
        string hora = Texto(e, "time");
        if (hora.Length == 0) hora = "12:00:00Z";
        return DateTimeOffset.Parse($"{fecha}T{hora}", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
    }

    // ---------- Trazado del circuito ----------

    /// <summary>Busca en el archivo de circuitos el más cercano a las coordenadas y lo convierte en un dibujo SVG.</summary>
    async Task<(string? Trazado, string? Meta)> TrazadoAsync(double latitud, double longitud)
    {
        try
        {
            string ruta = Path.Combine(carpetaProyecto, "cache-circuitos.geojson");
            // El archivo de circuitos casi nunca cambia: se descarga como mucho una vez al mes
            if (!File.Exists(ruta) || File.GetLastWriteTime(ruta) < DateTime.Now.AddDays(-30))
                await File.WriteAllTextAsync(ruta, await http.GetStringAsync(config.Circuitos));

            using JsonDocument doc = JsonDocument.Parse(await File.ReadAllTextAsync(ruta));
            List<(double Lon, double Lat)>? mejor = null;
            double distanciaMejor = double.MaxValue;

            foreach (JsonElement circuito in doc.RootElement.GetProperty("features").EnumerateArray())
            {
                JsonElement geometria = circuito.GetProperty("geometry");
                if (geometria.GetProperty("type").GetString() != "LineString") continue;

                var puntos = geometria.GetProperty("coordinates").EnumerateArray()
                                      .Select(p => (p[0].GetDouble(), p[1].GetDouble())).ToList();
                double distancia = Kilometros(latitud, longitud, puntos.Average(p => p.Item2), puntos.Average(p => p.Item1));
                if (distancia < distanciaMejor) { distanciaMejor = distancia; mejor = puntos; }
            }

            // Si el más cercano está a más de 10 km, el circuito no está en el archivo
            return mejor is null || distanciaMejor > 10 ? (null, null) : Dibujar(mejor);
        }
        catch
        {
            return (null, null);
        }
    }

    static double Kilometros(double lat1, double lon1, double lat2, double lon2)
    {
        double rad(double g) => g * Math.PI / 180;
        double dLat = rad(lat2 - lat1), dLon = rad(lon2 - lon1);
        double a = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(rad(lat1)) * Math.Cos(rad(lat2)) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 6371 * 2 * Math.Asin(Math.Sqrt(a));
    }

    /// <summary>Convierte las coordenadas en una ruta SVG que encaja en el recuadro de 220 × 130 de la web.</summary>
    static (string, string) Dibujar(List<(double Lon, double Lat)> puntos)
    {
        const double ancho = 220, alto = 130, margen = 12;
        double coseno = Math.Cos(puntos.Average(p => p.Lat) * Math.PI / 180);
        var xy = puntos.Select(p => (X: p.Lon * coseno, Y: -p.Lat)).ToList();

        double minX = xy.Min(p => p.X), maxX = xy.Max(p => p.X), minY = xy.Min(p => p.Y), maxY = xy.Max(p => p.Y);
        double escala = Math.Min((ancho - 2 * margen) / (maxX - minX), (alto - 2 * margen) / (maxY - minY));
        double desX = (ancho - (maxX - minX) * escala) / 2, desY = (alto - (maxY - minY) * escala) / 2;
        var plano = xy.Select(p => (X: (p.X - minX) * escala + desX, Y: (p.Y - minY) * escala + desY)).ToList();

        // Se descartan los puntos casi pegados al anterior para que el dibujo pese poco
        var ruta = new StringBuilder();
        (double X, double Y) ultimo = plano[0];
        ruta.Append(FormattableString.Invariant($"M{ultimo.X:0.#} {ultimo.Y:0.#}"));
        foreach (var p in plano.Skip(1))
        {
            if (Math.Abs(p.X - ultimo.X) + Math.Abs(p.Y - ultimo.Y) < 1.2) continue;
            ruta.Append(FormattableString.Invariant($" L{p.X:0.#} {p.Y:0.#}"));
            ultimo = p;
        }
        ruta.Append(" Z");

        // Línea de meta: en el primer punto del trazado, perpendicular a la dirección de la pista
        var inicio = plano[0];
        var siguiente = plano.Skip(1).FirstOrDefault(p => Math.Abs(p.X - inicio.X) + Math.Abs(p.Y - inicio.Y) > 0.5, plano[^1]);
        double dx = siguiente.X - inicio.X, dy = siguiente.Y - inicio.Y, largo = Math.Sqrt(dx * dx + dy * dy);
        double px = -dy / largo * 7, py = dx / largo * 7;
        string meta = FormattableString.Invariant($"M{inicio.X - px:0.#} {inicio.Y - py:0.#} L{inicio.X + px:0.#} {inicio.Y + py:0.#}");

        return (ruta.ToString(), meta);
    }
}

/// <summary>Nombres en español de los Grandes Premios y los países.</summary>
public static class Traducir
{
    static readonly Dictionary<string, string> Lugares = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Australian"] = "Australia", ["Chinese"] = "China", ["Japanese"] = "Japón", ["Bahrain"] = "Baréin",
        ["Saudi Arabian"] = "Arabia Saudí", ["Miami"] = "Miami", ["Canadian"] = "Canadá", ["Monaco"] = "Mónaco",
        ["Spanish"] = "España", ["Barcelona"] = "Barcelona", ["Barcelona-Catalunya"] = "Barcelona", ["Austrian"] = "Austria",
        ["British"] = "Gran Bretaña", ["Belgian"] = "Bélgica", ["Hungarian"] = "Hungría", ["Dutch"] = "Países Bajos",
        ["Italian"] = "Italia", ["Emilia Romagna"] = "Emilia-Romaña", ["Azerbaijan"] = "Azerbaiyán", ["Singapore"] = "Singapur",
        ["United States"] = "Estados Unidos", ["Mexico City"] = "Ciudad de México", ["Mexican"] = "México",
        ["São Paulo"] = "São Paulo", ["Brazilian"] = "Brasil", ["Las Vegas"] = "Las Vegas", ["Qatar"] = "Catar",
        ["Abu Dhabi"] = "Abu Dabi", ["Portuguese"] = "Portugal", ["Turkish"] = "Turquía", ["French"] = "Francia",
        ["German"] = "Alemania", ["Russian"] = "Rusia", ["Madrid"] = "Madrid"
    };

    static readonly Dictionary<string, string> Paises = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Japan"] = "Japón", ["Bahrain"] = "Baréin", ["Saudi Arabia"] = "Arabia Saudí", ["USA"] = "Estados Unidos",
        ["United States"] = "Estados Unidos", ["Canada"] = "Canadá", ["Monaco"] = "Mónaco", ["Spain"] = "España",
        ["UK"] = "Reino Unido", ["United Kingdom"] = "Reino Unido", ["Belgium"] = "Bélgica", ["Hungary"] = "Hungría",
        ["Netherlands"] = "Países Bajos", ["Italy"] = "Italia", ["Azerbaijan"] = "Azerbaiyán", ["Singapore"] = "Singapur",
        ["Mexico"] = "México", ["Brazil"] = "Brasil", ["Qatar"] = "Catar", ["UAE"] = "Emiratos Árabes Unidos",
        ["United Arab Emirates"] = "Emiratos Árabes Unidos", ["Turkey"] = "Turquía", ["France"] = "Francia",
        ["Germany"] = "Alemania", ["Russia"] = "Rusia",
        // Nombres que usan las webs de F2 y F3 en su calendario
        ["Great Britain"] = "Gran Bretaña", ["Abu Dhabi"] = "Abu Dabi", ["Barcelona-Catalunya"] = "España",
        ["Miami"] = "Estados Unidos", ["Australia"] = "Australia", ["Austria"] = "Austria"
    };

    /// <summary>"Mexico City Grand Prix" → "GP de Ciudad de México".</summary>
    public static string Carrera(string nombre)
    {
        string lugar = nombre.Replace("Grand Prix", "", StringComparison.OrdinalIgnoreCase).Trim();
        return Lugares.TryGetValue(lugar, out string? traducido) ? $"GP de {traducido}" : nombre;
    }

    public static string Pais(string pais) => Paises.GetValueOrDefault(pais, pais);
}
