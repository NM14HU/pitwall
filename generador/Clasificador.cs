using System.Text.RegularExpressions;

namespace Generador;

/// <summary>
/// Decide a qué secciones pertenece cada noticia (F1, Alonso, Sainz, F2, F3, FE)
/// a partir del feed del que viene y de las palabras de su titular y resumen.
/// </summary>
public sealed class Clasificador
{
    // Mismo orden que las secciones de la web
    static readonly string[] OrdenSecciones = ["F1", "Alonso", "Sainz", "F2", "F3", "FE"];
    static readonly string[] OtrasCategorias = ["F2", "F3", "FE"];
    static readonly string[] Pilotos = ["Alonso", "Sainz"];

    readonly Dictionary<string, (Regex Incluir, Regex? Excluir, Regex? Fuertes)> reglas;

    public Clasificador(Dictionary<string, Regla> reglasConfig)
    {
        reglas = reglasConfig
            .Where(r => r.Value.Incluir.Count > 0)
            .ToDictionary(r => r.Key, r => (Construir(r.Value.Incluir)!, Construir(r.Value.Excluir), Construir(r.Value.Fuertes)));
    }

    public static int Orden(string seccion) =>
        Array.IndexOf(OrdenSecciones, seccion) is var i and >= 0 ? i : 99;

    public List<string> Clasificar(FuenteFeed fuente, string texto)
    {
        var etiquetas = new HashSet<string>();

        // 1. Otras categorías: por el feed o porque el texto las menciona
        bool porFeed(string seccion) => fuente.Secciones.Contains(seccion) && !fuente.ExigirMencion;

        foreach (string categoria in OtrasCategorias)
            if (porFeed(categoria) || Coincide(categoria, texto))
                etiquetas.Add(categoria);

        // 2. Fórmula 1: si viene de un feed de F1, aunque mencione otra categoría
        //    (p. ej. «una promesa de F3 debutará en la F1» es de F1 y de F3).
        //    En los feeds generales (Filtrar) y en las búsquedas (ExigirMencion) tiene que hablar de F1.
        bool hablaDeF1 = Coincide("F1", texto);
        bool deOtraCompeticion = Pilotos.Any(p => Excluido(p, texto)) && !hablaDeF1;   // p. ej. Sainz en el Dakar
        bool feedDeF1 = fuente.Secciones.Contains("F1");
        bool exigeHablarDeF1 = fuente.Filtrar || fuente.ExigirMencion;

        if (feedDeF1 && !deOtraCompeticion && (!exigeHablarDeF1 || hablaDeF1))
            etiquetas.Add("F1");
        else if (fuente.Filtrar && hablaDeF1)
            etiquetas.Add("F1");

        // 3. Pilotos: por su nombre en el texto, o porque el feed es solo suyo (Google Noticias)
        foreach (string piloto in Pilotos)
        {
            bool mencionado = Coincide(piloto, texto);
            bool feedDelPiloto = porFeed(piloto) && !Excluido(piloto, texto);
            if (mencionado || feedDelPiloto)
            {
                etiquetas.Add(piloto);
                // Alonso y Sainz corren en F1: si no tiene otra sección, va también a F1
                if (!etiquetas.Overlaps(OtrasCategorias)) etiquetas.Add("F1");
            }
        }

        return etiquetas.OrderBy(Orden).ToList();
    }

    // Cuenta si aparece alguna palabra de la sección y ninguna excluida; una palabra «fuerte»
    // (p. ej. «Fórmula 1») hace que cuente aunque también haya una excluida (p. ej. «MotoGP»)
    bool Coincide(string seccion, string texto) =>
        reglas.TryGetValue(seccion, out var r) && r.Incluir.IsMatch(texto)
        && (!(r.Excluir?.IsMatch(texto) ?? false) || (r.Fuertes?.IsMatch(texto) ?? false));

    bool Excluido(string seccion, string texto) =>
        reglas.TryGetValue(seccion, out var r) && (r.Excluir?.IsMatch(texto) ?? false);

    /// <summary>Crea una expresión que busca palabras completas, sin distinguir mayúsculas.</summary>
    static Regex? Construir(List<string> palabras)
    {
        if (palabras.Count == 0) return null;
        string alternativas = string.Join("|", palabras.Select(Regex.Escape));
        return new Regex($@"(?<![\p{{L}}\p{{N}}])({alternativas})(?![\p{{L}}\p{{N}}])",
                         RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    }
}
