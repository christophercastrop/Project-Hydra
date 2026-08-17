using CaeManager.Application.Bandeja.Queries.ObtenerBandejaGestor;
using CaeManager.Web.Components.DesignSystem;

namespace CaeManager.Web.Features.Bandeja;

/// <summary>
/// Traduce TipoItemBandeja a color/etiqueta/texto de la acción primaria —
/// mismo espíritu que EstadoDocumentoUi, un solo sitio de traducción.
/// </summary>
public static class TipoItemBandejaUi
{
    /// <summary>
    /// Recibe el ítem completo, no solo el tipo — un RequisitoPendiente con
    /// EsAltaNueva (ver DocumentacionBloqueantePendienteDto.EsAltaNueva) no es
    /// un bloqueo que haya que corregir, es una alta que todavía no se ha
    /// completado: mismo tono que un documento simplemente pendiente, no el
    /// rojo de "algo se rompió".
    /// </summary>
    public static TonoBadge Tono(ItemBandejaDto item) => item.Tipo switch
    {
        TipoItemBandeja.SugerenciaVisitaUrgente => TonoBadge.Peligro,
        TipoItemBandeja.Faltante => TonoBadge.Peligro,
        TipoItemBandeja.Vencido => TonoBadge.Peligro,
        TipoItemBandeja.RequisitoPendiente => item.EsAltaNueva ? TonoBadge.Advertencia : TonoBadge.Peligro,
        TipoItemBandeja.VisitaUrgente => TonoBadge.Advertencia,
        TipoItemBandeja.Urgente => TonoBadge.Advertencia,
        TipoItemBandeja.RevisionIa => TonoBadge.Advertencia,
        TipoItemBandeja.DeteccionPendiente => TonoBadge.Advertencia,
        // Mismo tono que ya usa PlataformaTab.razor para EstadoAcreditacion.PendienteDeSubir.
        TipoItemBandeja.PlataformaPendiente => TonoBadge.Advertencia,
        _ => TonoBadge.Neutro
    };

    /// <summary>Overload por tipo puro — usado donde no hay un ítem concreto a mano (p. ej. agrupar recuentos por severidad en GrupoCola).</summary>
    public static TonoBadge Tono(TipoItemBandeja tipo) => Tono(new ItemBandejaDto(
        Id: "", Tipo: tipo, Titulo: "", Subtitulo: "", Fecha: null,
        TrabajadorId: null, CentroId: null, DocumentoId: null, TipoDocumentoId: null, RequisitoId: null));

    public static string Texto(ItemBandejaDto item) => item.Tipo switch
    {
        TipoItemBandeja.SugerenciaVisitaUrgente => "Visita sorpresa",
        TipoItemBandeja.Faltante => "Falta",
        TipoItemBandeja.Vencido => "Vencido",
        // "Bloquea el centro" implica una regresión a corregir; una alta
        // nueva nunca llegó a completarse, así que "Alta pendiente" describe
        // mejor la situación real (ver EsAltaNueva).
        TipoItemBandeja.RequisitoPendiente => item.EsAltaNueva ? "Alta pendiente" : "Bloquea el centro",
        TipoItemBandeja.VisitaUrgente => "Visita próxima",
        TipoItemBandeja.Urgente => "Urgente",
        TipoItemBandeja.RevisionIa => "Revisión IA",
        TipoItemBandeja.DeteccionPendiente => "Detección de personal",
        // No "Falta": la documentación existe y está al día en Talveg, solo
        // falta replicarla en la plataforma del cliente — un badge "Falta"
        // sugeriría (incorrectamente) que hay que reclamarla a alguien.
        TipoItemBandeja.PlataformaPendiente => "Pendiente",
        _ => "—"
    };

    /// <summary>
    /// Recibe el ítem completo, no solo el tipo — PlataformaPendiente necesita
    /// el nombre real de la plataforma ("Subir a Dokify"), que es un dato del
    /// ítem (ItemBandejaDto.ProveedorNombre), no un texto fijo por tipo como
    /// el resto de acciones; RequisitoPendiente con EsAltaNueva ofrece
    /// directamente "Adjuntar" el documento que falta, en vez de mandar a ver
    /// un requisito que en realidad nunca se llegó a cumplir.
    /// </summary>
    public static string TextoAccion(ItemBandejaDto item) => item.Tipo switch
    {
        TipoItemBandeja.Faltante => "Subir documento",
        TipoItemBandeja.RevisionIa => "Revisar",
        TipoItemBandeja.RequisitoPendiente => item.EsAltaNueva ? "Adjuntar" : "Ver requisito",
        TipoItemBandeja.SugerenciaVisitaUrgente => "Confirmar visita",
        TipoItemBandeja.VisitaUrgente => "Ver visita",
        TipoItemBandeja.DeteccionPendiente => "Revisar detección",
        TipoItemBandeja.PlataformaPendiente => $"Subir a {item.ProveedorNombre}",
        _ => "Gestionar"
    };
}
