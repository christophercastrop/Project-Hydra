using CaeManager.Application.Bandeja.Queries.ObtenerBandejaAgrupada;
using CaeManager.Application.Bandeja.Queries.ObtenerBandejaGestor;
using CaeManager.Application.Common;
using CaeManager.Application.Dashboard.Queries;
using CaeManager.Application.Reclamaciones.Queries.ObtenerReclamacionesSinRespuesta;
using CaeManager.Application.Tenants.Queries.ObtenerPerfilVocabularioActual;
using CaeManager.Application.Visitas.Queries.ObtenerVisitas;
using CaeManager.Domain.Tenants;
using CaeManager.Infrastructure.Identity;
using CaeManager.Web.Services;
using MediatR;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;

namespace CaeManager.Web.Features.Dashboard.Pages;

/// <summary>
/// Inicio (SCREEN 01 de la auditoría de producto 2026-08-16, Parte XV) —
/// sustituye al Dashboard anterior en "/" (Dashboard.razor(.cs/.css)
/// eliminados). Trae dos piezas que el Dashboard antiguo no tenía:
/// "Requiere atención" agrupado por situación
/// (<see cref="ObtenerBandejaAgrupadaQuery"/>, hallazgo P-03) y "Pendiente
/// por plataforma" (<see cref="ObtenerPendientePorPlataformaQuery"/>,
/// hallazgo P-04).
/// </summary>
public partial class Inicio : ComponentBase
{
    private const int MaximoGruposAtencion = 5;
    private const int MaximoVisitasProximas = 3;

    [Inject] private IMediator Mediator { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;
    [Inject] private NavigationManager NavigationManager { get; set; } = default!;
    [Inject] private ActividadUsuarioService ActividadUsuario { get; set; } = default!;
    [Inject] private UserManager<ApplicationUser> UserManager { get; set; } = default!;
    [Inject] private PuertaAccesoDatos PuertaAccesoDatos { get; set; } = default!;

    private KpisDashboardDto? _kpis;
    private BandejaAgrupadaDto? _bandejaAgrupada;
    private IReadOnlyList<PendientePorPlataformaDto> _pendientePorPlataforma = [];
    private IReadOnlyList<ItemBandejaDto> _queLlegoSinVer = [];
    private IReadOnlyList<VisitaListaDto> _proximamente = [];
    private ProximoVencimientoDto? _proximoVencimiento;
    private PulsoEquipoDto? _pulso;
    private IReadOnlyList<ReclamacionSinRespuestaDto> _sinRespuesta = [];
    private string? _nombrePila;
    private bool _error;
    private PerfilVocabularioTenant _perfilVocabulario = PerfilVocabularioTenant.ClienteDirecto;

    /// <summary>
    /// Consultora gestiona la CAE de varias Empresas contratistas bajo un
    /// mismo Cliente titular — "Requiere atención" necesita el nivel
    /// Empresa→Trabajador para no mezclarlas en una sola lista. ClienteDirecto
    /// ("empresa final", DDL-072) ES la Empresa: ese nivel sería redundante,
    /// solo Trabajador.
    /// </summary>
    private bool AgruparPorEmpresa => _perfilVocabulario == PerfilVocabularioTenant.Consultora;

    // "Requiere atención" reutiliza la Bandeja del gestor — visible a todos
    // los roles salvo Cliente.
    private bool _mostrarRequiereAtencion;

    protected override Task OnInitializedAsync() => CargarAsync();

    /// <summary>
    /// El nombre de pila se resuelve aparte, tras el primer render, y NO
    /// dentro de CargarAsync — UserManager.GetUserAsync usa el mismo
    /// DbContext con ámbito de circuito que el resto de componentes del
    /// shell (MainLayout: SelectorClienteActivo, ContextWorkspace…), que
    /// también corren su propia inicialización en paralelo durante la carga
    /// inicial. Mover la llamada a OnAfterRenderAsync reduce la ventana pero
    /// no la cierra del todo — reproducido en vivo ("a second operation was
    /// started on this context instance", seguido de ObjectDisposedException
    /// al liberar un semáforo de PuertaAccesoDatos ya liberado por la
    /// carrera). El acceso directo a UserManager tiene que pasar por la
    /// puerta, igual que ya hace MainLayout.razor.cs con la suya — ver
    /// PuertaAccesoDatos.cs: "los accesos directos (UserManager en páginas y
    /// layout...) se envuelven en su sitio".
    /// </summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        var estadoAutenticacion = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        var nombrePila = await PuertaAccesoDatos.EjecutarAsync(async () =>
        {
            var usuario = await UserManager.GetUserAsync(estadoAutenticacion.User);
            // Identity.Name es el email de acceso (mismo dato que usa el enlace "Mi
            // firma" del topbar), no un nombre para mostrar; NombreCompleto es el
            // campo real (mismo origen que PestanaHistorial usa para "quién hizo
            // esto"). Sin NombreCompleto, se omite el nombre del saludo en vez de
            // caer en el email — "Buenos días, refri.gestorcae1@..." es peor que no
            // personalizar. Solo el nombre de pila, como en el mockup ("Buenos días,
            // Marta") — el nombre completo en un saludo de cabecera lee como un formulario.
            return usuario?.NombreCompleto is { Length: > 0 } nombreCompleto
                ? nombreCompleto.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
                : null;
        });

        if (nombrePila != _nombrePila)
        {
            _nombrePila = nombrePila;
            StateHasChanged();
        }
    }

    private string Saludo => _nombrePila is { Length: > 0 }
        ? $"{SaludoBase}, {_nombrePila}"
        : SaludoBase;

    private static string SaludoBase => DateTime.Now.Hour switch
    {
        < 12 => "Buenos días",
        < 20 => "Buenas tardes",
        _ => "Buenas noches"
    };

    private async Task CargarAsync()
    {
        _error = false;
        _kpis = null;
        StateHasChanged();

        try
        {
            var estadoAutenticacion = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            _mostrarRequiereAtencion = !estadoAutenticacion.User.IsInRole(Roles.Cliente);
            _perfilVocabulario = await Mediator.Send(new ObtenerPerfilVocabularioActualQuery());

            _kpis = await Mediator.Send(new ObtenerKpisDashboardQuery());

            if (!_kpis.SinCarteraAsignada)
            {
                var visitas = await Mediator.Send(new ObtenerVisitasQuery(
                    Busqueda: null, SoloActivas: true, NotificadoCliente: null, TamanoPagina: MaximoVisitasProximas));
                _proximamente = visitas.Elementos;

                _pendientePorPlataforma = await Mediator.Send(new ObtenerPendientePorPlataformaQuery());

                if (_mostrarRequiereAtencion)
                {
                    _bandejaAgrupada = await Mediator.Send(new ObtenerBandejaAgrupadaQuery());
                    var todosLosItems = _bandejaAgrupada.Grupos.SelectMany(g => g.Items).Concat(_bandejaAgrupada.SinGrupo).ToList();

                    // Resuelto una única vez por circuito en MainLayout — aquí solo se lee el
                    // resultado ya cacheado (docs/blueprints/OPERATIONAL-HOME.md § 6, DDL-068).
                    var (ausente, desde) = await ActividadUsuario.RegistrarYEvaluarAsync(RendererInfo.IsInteractive);
                    if (ausente && desde is { } desdeValor)
                        _queLlegoSinVer = [.. todosLosItems.Where(i => i.CreadaEnUtc > desdeValor)];

                    // El próximo vencimiento solo aporta algo cuando la cola ya está vacía.
                    if (todosLosItems.Count == 0)
                        _proximoVencimiento = (await Mediator.Send(new ObtenerDesgloseDashboardQuery())).ProximoVencimiento;

                    _pulso = await Mediator.Send(new ObtenerPulsoEquipoQuery());
                    _sinRespuesta = await Mediator.Send(new ObtenerReclamacionesSinRespuestaQuery());
                }
            }
        }
        catch (Exception)
        {
            _error = true;
        }
    }

    /// <summary>Base real de "N de M vigentes" (mockup Inicio TALVEG) — la misma suma que ya usa ObtenerKpisDashboardQuery para la tasa de cumplimiento, no una cifra nueva.</summary>
    private int TotalDocumentosConVigencia => _kpis is null ? 0
        : _kpis.DocumentosVencidos + _kpis.DocumentosUrgentes + _kpis.DocumentosProximos + _kpis.DocumentosVigentes;

    private int DocumentosFueraDeVigencia => _kpis is null ? 0
        : _kpis.DocumentosVencidos + _kpis.DocumentosUrgentes + _kpis.DocumentosProximos;

    /// <summary>Centros distintos con al menos un RequisitoPendiente en la cola ya cargada — sin query nueva, es el mismo dato que ya pinta "Requiere atención".</summary>
    private int CentrosBloqueados => _bandejaAgrupada is null ? 0
        : _bandejaAgrupada.Grupos.SelectMany(g => g.Items).Concat(_bandejaAgrupada.SinGrupo)
            .Where(i => i.Tipo == TipoItemBandeja.RequisitoPendiente && i.CentroId is not null)
            .Select(i => i.CentroId!.Value)
            .Distinct()
            .Count();

    private string TextoCierreAtencion =>
        _proximoVencimiento is { } proximo
            ? $"Nada pendiente ahora mismo. Próximo vencimiento: {proximo.TipoDocumentoNombre} de {proximo.TrabajadorNombre}, el {proximo.FechaVencimiento:dd/MM/yyyy} ({TextoDias(proximo.DiasRestantes)})."
            : "Nada pendiente ahora mismo.";

    private static string TextoDias(int dias) => dias switch
    {
        0 => "hoy",
        1 => "en 1 día",
        _ => $"en {dias} días"
    };
}
