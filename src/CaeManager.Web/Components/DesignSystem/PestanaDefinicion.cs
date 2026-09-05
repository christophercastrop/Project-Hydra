namespace CaeManager.Web.Components.DesignSystem;

/// <summary>
/// Una pestaña de <see cref="Pestanas"/> — Id estable (usado en la URL/estado),
/// Etiqueta visible y, opcionalmente, el nombre de un icono del catálogo.
///
/// <para>
/// El icono es opcional y se decide <b>por tira completa</b>, no por pestaña:
/// una sola pestaña con icono en una fila que no los lleva se lee como un
/// defecto, no como énfasis. O las lleva todas o ninguna.
/// </para>
/// </summary>
public record PestanaDefinicion(string Id, string Etiqueta, string? Icono = null);
