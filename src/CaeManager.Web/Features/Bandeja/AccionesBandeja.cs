using CaeManager.Application.Bandeja.Queries.ObtenerBandejaGestor;
using CaeManager.Web.Components.Workspace;
using Microsoft.AspNetCore.Components;

namespace CaeManager.Web.Features.Bandeja;

/// <summary>
/// La acción primaria de cada tarjeta de la Bandeja del gestor — compartida
/// entre <c>/bandeja</c> y el panel montado en <c>/alertas</c>, para que
/// ambos lugares abran exactamente el mismo sitio ante el mismo ítem.
/// </summary>
public static class AccionesBandeja
{
    public static Task AbrirAsync(ItemBandejaDto item, NavigationManager navigationManager, ContextWorkspaceService workspaceService) => item.Tipo switch
    {
        // Falta/Vencido/Urgente vienen de ObtenerAlertasQuery — mismo destino
        // que ya usa GestionarAlerta en Alertas.razor.cs.
        TipoItemBandeja.RevisionIa => Navegar(navigationManager, "/documentos/revision-ia"),
        TipoItemBandeja.RequisitoPendiente => AbrirRequisitoAsync(item, workspaceService),
        // Mismo destino que ya usa el botón "Crear visita" de la Bandeja de
        // Comunicaciones — el Drawer de /visitas prellena los datos.
        TipoItemBandeja.SugerenciaVisitaUrgente => Navegar(navigationManager, $"/visitas?sugerenciaId={item.SugerenciaVisitaId}"),
        // Sin deep-link a una Visita concreta todavía (el Drawer de detalle
        // es estado interno de /visitas, no hay ruta por Id) — abre la lista
        // ya filtrada por urgencia es lo más cercano disponible hoy.
        TipoItemBandeja.VisitaUrgente => Navegar(navigationManager, "/visitas"),
        TipoItemBandeja.DeteccionPendiente => Navegar(navigationManager, $"/empresas/{item.EmpresaId}/deteccion-trabajadores"),
        // La acción real (MarcarAcreditacionSubidaCommand) vive en la pestaña
        // Plataforma de /documentos, no en DocumentoWorkspacePanel (el
        // fallback genérico de abajo) — ese panel no tiene ningún control de
        // acreditación por plataforma.
        TipoItemBandeja.PlataformaPendiente => Navegar(navigationManager, "/documentos?pestana=plataforma"),
        _ => Navegar(navigationManager, item.DocumentoId is { } documentoId
            ? $"/documentos?documentoId={documentoId}"
            : $"/documentos?trabajadorId={item.TrabajadorId}&tipoDocumentoId={item.TipoDocumentoId}")
    };

    private static Task AbrirRequisitoAsync(ItemBandejaDto item, ContextWorkspaceService workspaceService) =>
        item.CentroId is { } centroId
            ? workspaceService.AbrirAsync(EntidadWorkspace.Centro, centroId, item.Subtitulo, "requisitos")
            : Task.CompletedTask;

    private static Task Navegar(NavigationManager navigationManager, string url)
    {
        navigationManager.NavigateTo(url);
        return Task.CompletedTask;
    }
}
