using CaeManager.Application.Bandeja.Queries.ObtenerBandejaAgrupada;
using CaeManager.Application.Bandeja.Queries.ObtenerBandejaGestor;
using CaeManager.Application.Tenants.Queries.ObtenerPerfilVocabularioActual;
using CaeManager.Domain.Tenants;
using CaeManager.Web.Components;
using Microsoft.AspNetCore.Components;

namespace CaeManager.Web.Features.Bandeja.Pages;

public partial class Bandeja : ComponentBase
{
    [Inject] private NavigationManager NavigationManager { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "tipo")]
    public string? TipoInicial { get; set; }

    private enum OrdenBandeja { Impacto, Fecha }

    private BandejaAgrupadaDto? _bandeja;
    private string _tipoFiltro = string.Empty;
    private OrdenBandeja _orden = OrdenBandeja.Impacto;
    private bool _agruparPorEmpresa;
    private bool _cargando = true;
    private bool _errorCarga;
    private string? _idEnfocado;

    /// <summary>Todos los items sueltos, sin filtrar ni agrupar — base para las cuentas de cada chip (siempre sobre el total, nunca sobre lo ya filtrado) y para j/k/Enter.</summary>
    private IReadOnlyList<ItemBandejaDto> Items =>
        _bandeja is null ? [] : [.. _bandeja.Grupos.SelectMany(g => g.Items), .. _bandeja.SinGrupo];

    private IReadOnlyList<ItemBandejaDto> ItemsFiltrados =>
        Enum.TryParse<TipoItemBandeja>(_tipoFiltro, out var tipo)
            ? Items.Where(i => i.Tipo == tipo).ToList()
            : Items;

    /// <summary>
    /// Los grupos ya vienen ordenados "por impacto" (bloquea acceso primero,
    /// luego severidad, ver ObtenerBandejaAgrupadaQueryHandler.Agrupar) —
    /// filtrar reduce los ITEMS de cada grupo (un grupo puede tener vencidos
    /// Y próximos a la vez), así que hay que reagrupar sobre
    /// ItemsFiltrados, no solo esconder grupos enteros. El botón "Fecha" del
    /// mockup reordena los GRUPOS por su vencimiento más próximo — el mismo
    /// criterio "qué urge antes" que Impacto, solo que por fecha en vez de
    /// por severidad.
    /// </summary>
    private IReadOnlyList<GrupoColaDto> GruposOrdenados
    {
        get
        {
            var agrupados = ObtenerBandejaAgrupadaQueryHandler.Agrupar(ItemsFiltrados).Grupos;
            return _orden == OrdenBandeja.Impacto
                ? agrupados
                : [.. agrupados.OrderBy(g => g.Items.Min(i => i.Fecha) ?? DateOnly.MaxValue)];
        }
    }

    private IReadOnlyList<(string Tipo, string Etiqueta, int Cantidad)> Chips =>
    [
        (string.Empty, "Todos", Items.Count),
        (nameof(TipoItemBandeja.SugerenciaVisitaUrgente), "Visita sorpresa", Contador(TipoItemBandeja.SugerenciaVisitaUrgente)),
        (nameof(TipoItemBandeja.Faltante), "Falta", Contador(TipoItemBandeja.Faltante)),
        (nameof(TipoItemBandeja.Vencido), "Vencido", Contador(TipoItemBandeja.Vencido)),
        (nameof(TipoItemBandeja.VisitaUrgente), "Visita próxima", Contador(TipoItemBandeja.VisitaUrgente)),
        (nameof(TipoItemBandeja.RequisitoPendiente), "Bloquea el centro", Contador(TipoItemBandeja.RequisitoPendiente)),
        (nameof(TipoItemBandeja.Urgente), "Urgente", Contador(TipoItemBandeja.Urgente)),
        (nameof(TipoItemBandeja.RevisionIa), "Revisión IA", Contador(TipoItemBandeja.RevisionIa)),
        (nameof(TipoItemBandeja.DeteccionPendiente), "Detección de personal", Contador(TipoItemBandeja.DeteccionPendiente)),
        (nameof(TipoItemBandeja.PlataformaPendiente), "Pendiente por plataforma", Contador(TipoItemBandeja.PlataformaPendiente)),
    ];

    protected override Task OnInitializedAsync() => CargarAsync();

    /// <summary>
    /// La URL es la fuente de verdad del filtro, no solo su semilla inicial
    /// — mismo patrón que el resto de listados (P1-18 de
    /// docs/business/MATURITY_REVIEW.md).
    /// </summary>
    protected override void OnParametersSet()
    {
        _tipoFiltro = !string.IsNullOrWhiteSpace(TipoInicial) && Enum.TryParse<TipoItemBandeja>(TipoInicial, out _)
            ? TipoInicial
            : string.Empty;
    }

    private Task CambiarFiltroAsync(string valor)
    {
        _tipoFiltro = valor;
        _idEnfocado = null;
        NavigationManager.ActualizarFiltroEnUrl("tipo", valor);
        return Task.CompletedTask;
    }

    private Task CambiarOrdenAsync(OrdenBandeja orden)
    {
        _orden = orden;
        return Task.CompletedTask;
    }

    private async Task CargarAsync()
    {
        _cargando = true;
        _errorCarga = false;
        StateHasChanged();

        try
        {
            _bandeja = await Mediator.Send(new ObtenerBandejaAgrupadaQuery());
            _agruparPorEmpresa = await Mediator.Send(new ObtenerPerfilVocabularioActualQuery()) == PerfilVocabularioTenant.Consultora;
        }
        catch (Exception)
        {
            _errorCarga = true;
        }
        finally
        {
            _cargando = false;
        }
    }

    private static bool CoincideFiltro(ItemBandejaDto item, string tipoFiltro) =>
        !Enum.TryParse<TipoItemBandeja>(tipoFiltro, out var tipo) || item.Tipo == tipo;

    /// <summary>H2 (docs/ux-audit/10-bandeja-alertas-calendario.md): "¿qué atiendo primero?" pide los números antes de filtrar, no después. Cuenta siempre sobre Items completo, nunca sobre ItemsFiltrados, para que el número de cada chip no cambie según cuál esté seleccionado.</summary>
    private int Contador(TipoItemBandeja tipo) => Items.Count(i => i.Tipo == tipo);

    private async Task ManejarAtajoAsync(string tecla)
    {
        var items = ItemsFiltrados;
        if (items.Count == 0) return;

        switch (tecla)
        {
            case "j":
                {
                    var indiceActual = _idEnfocado is null ? -1 : items.ToList().FindIndex(i => i.Id == _idEnfocado);
                    _idEnfocado = items[Math.Min(indiceActual + 1, items.Count - 1)].Id;
                    break;
                }
            case "k":
                {
                    var indiceActual = _idEnfocado is null ? 0 : items.ToList().FindIndex(i => i.Id == _idEnfocado);
                    _idEnfocado = items[Math.Max(indiceActual - 1, 0)].Id;
                    break;
                }
            case "Enter":
                if (_idEnfocado is { } idAbrir)
                {
                    var item = items.FirstOrDefault(i => i.Id == idAbrir);
                    if (item is not null)
                        await AccionesBandeja.AbrirAsync(item, NavigationManager, WorkspaceService);
                }
                break;
        }

        StateHasChanged();
    }
}
