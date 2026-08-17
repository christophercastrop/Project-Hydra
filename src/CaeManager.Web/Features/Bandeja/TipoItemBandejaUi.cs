using CaeManager.Application.Bandeja.Queries.ObtenerBandejaGestor;
using CaeManager.Web.Components.DesignSystem;

namespace CaeManager.Web.Features.Bandeja;

/// <summary>
/// Traduce TipoItemBandeja a color/etiqueta/texto de la acción primaria —
/// mismo espíritu que EstadoDocumentoUi, un solo sitio de traducción.
/// </summary>
public static class TipoItemBandejaUi
{
    public static TonoBadge Tono(TipoItemBandeja tipo) => tipo switch
    {
        TipoItemBandeja.SugerenciaVisitaUrgente => TonoBadge.Peligro,
        TipoItemBandeja.Faltante => TonoBadge.Peligro,
        TipoItemBandeja.Vencido => TonoBadge.Peligro,
        TipoItemBandeja.RequisitoPendiente => TonoBadge.Peligro,
        TipoItemBandeja.VisitaUrgente => TonoBadge.Advertencia,
        TipoItemBandeja.Urgente => TonoBadge.Advertencia,
        TipoItemBandeja.RevisionIa => TonoBadge.Advertencia,
        TipoItemBandeja.DeteccionPendiente => TonoBadge.Advertencia,
        // Mismo tono que ya usa PlataformaTab.razor para EstadoAcreditacion.PendienteDeSubir.
        TipoItemBandeja.PlataformaPendiente => TonoBadge.Advertencia,
        _ => TonoBadge.Neutro
    };

    public static string Texto(TipoItemBandeja tipo) => tipo switch
    {
        TipoItemBandeja.SugerenciaVisitaUrgente => "Visita sorpresa",
        TipoItemBandeja.Faltante => "Falta",
        TipoItemBandeja.Vencido => "Vencido",
        TipoItemBandeja.RequisitoPendiente => "Bloquea el centro",
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
    /// el resto de acciones.
    /// </summary>
    public static string TextoAccion(ItemBandejaDto item) => item.Tipo switch
    {
        TipoItemBandeja.Faltante => "Subir documento",
        TipoItemBandeja.RevisionIa => "Revisar",
        TipoItemBandeja.RequisitoPendiente => "Ver requisito",
        TipoItemBandeja.SugerenciaVisitaUrgente => "Confirmar visita",
        TipoItemBandeja.VisitaUrgente => "Ver visita",
        TipoItemBandeja.DeteccionPendiente => "Revisar detección",
        TipoItemBandeja.PlataformaPendiente => $"Subir a {item.ProveedorNombre}",
        _ => "Gestionar"
    };
}
