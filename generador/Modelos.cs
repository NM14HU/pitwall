using System.Text.Json;

namespace Generador;

/// <summary>Una noticia tal y como se guarda en noticias.json.</summary>
public sealed class Noticia
{
    public string Titulo { get; set; } = "";
    public string Resumen { get; set; } = "";
    public string Medio { get; set; } = "";
    public string Url { get; set; } = "";
    public string Imagen { get; set; } = "";
    public DateTimeOffset Fecha { get; set; }
    public List<string> Etiquetas { get; set; } = [];

    /// <summary>"es" o "en": la web permite al visitante ver solo las de español o todas.</summary>
    public string Idioma { get; set; } = "es";
}

/// <summary>Contenido de feeds.json.</summary>
public sealed class Configuracion
{
    public string Salida { get; set; } = "../web/data/noticias.json";
    public int MaxNoticias { get; set; } = 300;

    /// <summary>Máximo de noticias de cada sección (las más recientes).</summary>
    public int MaxPorSeccion { get; set; } = 40;
    public int DiasMaximos { get; set; } = 7;

    /// <summary>"completo" (hasta 2 líneas), "corto" (1 línea) o "no".</summary>
    public string Resumen { get; set; } = "completo";

    /// <summary>Mostrar la foto que trae cada noticia en su feed.</summary>
    public bool Imagenes { get; set; } = true;

    /// <summary>Si el feed no trae foto, buscarla en la página del artículo (og:image).</summary>
    public bool BuscarImagenEnArticulo { get; set; } = true;
    public Dictionary<string, Regla> Reglas { get; set; } = [];
    public List<FuenteFeed> Feeds { get; set; } = [];
    public ConfigMundial Mundial { get; set; } = new();

    /// <summary>Nombres de medio que se sustituyen por otro (p. ej. "Diario AS" → "AS").</summary>
    public Dictionary<string, string> NombresMedios { get; set; } = [];

    /// <summary>
    /// Escribe el nombre del medio siempre igual: primero la lista NombresMedios y, si no está,
    /// el nombre que tenga en la lista de feeds sin mirar mayúsculas ("MARCA" → "Marca").
    /// </summary>
    public string NombreMedio(string medio)
    {
        string limpio = medio.Trim();
        foreach (var (original, nombre) in NombresMedios)
            if (string.Equals(original, limpio, StringComparison.OrdinalIgnoreCase)) return nombre;
        return Feeds.Select(f => f.Medio)
                    .FirstOrDefault(f => string.Equals(f, limpio, StringComparison.OrdinalIgnoreCase)) ?? limpio;
    }

    public static Configuracion Cargar(string ruta)
    {
        if (!File.Exists(ruta))
            throw new FileNotFoundException($"No encuentro el archivo de configuración: {ruta}");

        var opciones = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        return JsonSerializer.Deserialize<Configuracion>(File.ReadAllText(ruta), opciones)
               ?? throw new InvalidDataException("feeds.json está vacío o mal escrito");
    }
}

/// <summary>Palabras que hacen que una noticia pertenezca (o no) a una sección.</summary>
public sealed class Regla
{
    public List<string> Incluir { get; set; } = [];
    public List<string> Excluir { get; set; } = [];
}

/// <summary>Un feed de un medio.</summary>
public sealed class FuenteFeed
{
    public string Medio { get; set; } = "";
    public string Url { get; set; } = "";
    public List<string> Secciones { get; set; } = [];

    /// <summary>Feed general de motor: solo se quedan las noticias que hablen de nuestras secciones.</summary>
    public bool Filtrar { get; set; }

    /// <summary>
    /// Las noticias solo van a las secciones del feed si su titular las menciona.
    /// Para búsquedas de Google, que devuelven artículos que nombran al piloto en cualquier parte del texto.
    /// </summary>
    public bool ExigirMencion { get; set; }

    /// <summary>El nombre del medio viene en cada noticia (Google Noticias).</summary>
    public bool MedioDesdeNoticia { get; set; }

    public bool Activo { get; set; } = true;

    /// <summary>
    /// Solo se aceptan las noticias cuya dirección contenga este texto (p. ej. "/formula-e/").
    /// Sirve para medios que cuelan en un feed de una categoría artículos de otra.
    /// </summary>
    public string UrlContiene { get; set; } = "";

    /// <summary>Máximo de noticias que se toman de este feed (las más recientes). 0 = sin límite.</summary>
    public int Maximo { get; set; }
    public string Idioma { get; set; } = "es";
}

/// <summary>Una entrada leída de un feed, antes de clasificarla.</summary>
public sealed record EntradaFeed(string Titulo, string Resumen, string Url, string Imagen, DateTimeOffset Fecha, string Medio);

/// <summary>Resultado de leer un feed: si ha funcionado y qué ha traído.</summary>
public sealed record ResultadoFeed(FuenteFeed Fuente, bool Ok, string Mensaje, List<EntradaFeed> Entradas);
