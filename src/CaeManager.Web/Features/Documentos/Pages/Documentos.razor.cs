using System.Text.Json;
using CaeManager.Application.Clientes.Queries.ObtenerClientesParaSelector;
using CaeManager.Application.Configuracion.Commands.EliminarFiltroGuardado;
using CaeManager.Application.Configuracion.Commands.GuardarFiltro;
using CaeManager.Application.Configuracion.Queries;
using CaeManager.Application.Documentos.Commands.CrearDocumento;
using CaeManager.Application.Documentos.Commands.EliminarDocumento;
using CaeManager.Application.Documentos.Commands.EliminarDocumentos;
using CaeManager.Application.Documentos.Commands.RenovarDocumento;
using CaeManager.Application.Documentos.Commands.RestaurarDocumento;
using CaeManager.Application.Documentos.Queries.DetectarCamposDocumento;
using CaeManager.Application.Documentos.Queries.ObtenerDocumentoPorId;
using CaeManager.Application.Documentos.Queries.ObtenerDocumentos;
using CaeManager.Application.Empresas.Queries.ObtenerEmpresasParaSelector;
using CaeManager.Application.Proyectos.Queries.ObtenerProyectosParaSelector;
using CaeManager.Application.TiposDocumento.Queries.ObtenerTiposDocumento;
using CaeManager.Application.Trabajadores.Commands.AsignarAliasTrabajador;
using CaeManager.Application.Trabajadores.Queries.ObtenerTrabajadoresParaSelector;
using CaeManager.Application.Vehiculos.Queries.ObtenerVehiculosParaSelector;
using CaeManager.Domain.Documentos;
using CaeManager.Web.Components;
using CaeManager.Web.Components.DesignSystem;
using CaeManager.Web.Components.Workspace;
using CaeManager.Web.Documentos;
using CaeManager.Web.Features.Documentos.Components;
using FluentValidation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.QuickGrid;
using Microsoft.Extensions.Logging;

namespace CaeManager.Web.Features.Documentos.Pages;

public partial class Documentos : ComponentBase
{
    /// <summary>
    /// Permite llegar aquí desde Alertas o Calendario con un documento
    /// concreto ya listo para gestionar (p. ej. "/documentos?documentoId=...")
    /// en vez de obligar a buscarlo manualmente en la lista.
    /// </summary>
    [SupplyParameterFromQuery] public Guid? DocumentoId { get; set; }

    /// <summary>
    /// Permite llegar aquí desde el Dashboard con el filtro de Estado ya
    /// aplicado (p. ej. la tarjeta KPI "Vigentes" enlaza a
    /// "/documentos?estado=Vigente").
    /// </summary>
    [SupplyParameterFromQuery] public string? Estado { get; set; }

    /// <summary>
    /// Permite llegar aquí desde Alertas con un documento "faltante" (P1-15
    /// de docs/business/MATURITY_REVIEW.md — Trabajador con Asignación activa
    /// a un Centro que exige un TipoDocumento obligatorio, sin ningún
    /// Documento de ese tipo): abre el drawer de creación con el propietario
    /// y el tipo ya elegidos, en vez de "gestionar" un Documento que todavía
    /// no existe (DocumentoId, arriba, siempre es null en este caso).
    /// </summary>
    [SupplyParameterFromQuery] public Guid? TrabajadorId { get; set; }

    /// <summary>Misma idea que <see cref="TrabajadorId"/> pero para un documento "faltante" de Ámbito Empresa (ver Detalle de la visita).</summary>
    [SupplyParameterFromQuery] public Guid? EmpresaIdFaltante { get; set; }

    [SupplyParameterFromQuery] public Guid? TipoDocumentoId { get; set; }

    [SupplyParameterFromQuery(Name = "q")] public string? TerminoBusquedaInicial { get; set; }

    /// <summary>Ámbito del filtro de la rejilla — mismo mecanismo que <see cref="Estado"/>, ver OnParametersSet.</summary>
    [SupplyParameterFromQuery] public string? Ambito { get; set; }

    [Inject] private NavigationManager NavigationManager { get; set; } = default!;

    /// <summary>Comando del palette "Crear documento" (P3-31): /documentos?accion=crear abre el Drawer directamente.</summary>
    [SupplyParameterFromQuery] public string? Accion { get; set; }

    /// <summary>Pestaña con la que abrir la página — deep-link desde otras superficies (hoy, el timeline de Comunicaciones).</summary>
    [SupplyParameterFromQuery] public string? Pestana { get; set; }

    private GridItemsProvider<DocumentoListaDto>? _proveedorElementos;

    private static readonly IReadOnlyList<PestanaDefinicion> _pestanasDocumentos =
    [
        new("listado", "Listado", "documentos"),
        new("plataforma", "Plataforma", "plataforma"),
        // "correo" y "reloj" son los más cercanos del catálogo: no hay glifo
        // propio de reclamación (que sale por correo) ni de preventivo (que
        // es anticiparse al vencimiento). Si algún día se dibujan, aquí.
        new("reclamaciones", "Reclamaciones", "correo"),
        new("sugerencias", "Preventivo", "reloj"),
        new("revision-ia", "Revisión IA", "ia"),
        new("plantillas", "Plantillas", "plantilla")
    ];

    private string _pestanaActiva = "listado";

    /// <summary>
    /// La pestaña se refleja en la URL (mismo mecanismo que los filtros de la
    /// rejilla, P1-18) para que recargar, compartir el enlace o navegar
    /// atrás/adelante no pierda la sección elegida — hallazgo de revisión
    /// adversarial de Codex tras plegar Revisión IA en pestaña: antes tenía
    /// ruta propia y por tanto URL durable por definición. "listado" no se
    /// escribe en la URL por ser el valor por defecto.
    /// </summary>
    private void CambiarPestana(string pestana)
    {
        _pestanaActiva = pestana;
        NavigationManager.ActualizarFiltroEnUrl(nameof(Pestana), pestana == "listado" ? null : pestana);
    }

    // --- P3-31: selección múltiple, atajos j/k, filtros guardados ---
    private readonly HashSet<Guid> _seleccionados = [];

    /// <summary>
    /// Los checkboxes de fila solo se pintan con esto activo (Centro 360,
    /// PLAN-EJECUCION-UX.md § 0.9) — son ruido permanente para una acción
    /// ocasional. Apagarlo limpia la selección: dejar filas marcadas que ya
    /// no se ven dejaría la barra de acciones en lote apuntando a algo
    /// invisible.
    /// </summary>
    private bool _seleccionMultiple;

    private void AlternarSeleccionMultiple(bool activa)
    {
        _seleccionMultiple = activa;
        if (!activa)
            _seleccionados.Clear();
    }
    private List<DocumentoListaDto> _elementosPagina = [];
    private Guid? _idEnfocado;
    private bool _eliminandoLote;
    private bool _confirmarEliminarLoteVisible;

    private IReadOnlyList<FiltroGuardadoDto> _filtrosGuardados = [];
    private bool _mostrarGuardarFiltro;
    private string _nombreFiltroNuevo = string.Empty;
    private bool _guardandoFiltro;

    private record FiltrosDocumentosJson(string? Busqueda, string? Ambito, string? Estado);

    private DrawerGestionDocumento _drawerGestion = default!;

    protected override async Task OnInitializedAsync()
    {
        // Delegado estable — ver Clientes.razor.cs (bucle de recargas de QuickGrid).
        _proveedorElementos = ProveerElementosAsync;

        _filtrosGuardados = await Mediator.Send(new ObtenerFiltrosGuardadosQuery(PantallasConFiltrosGuardados.Documentos));
    }

    /// <summary>
    /// _drawerGestion (@ref) todavía no está asignado durante OnInitializedAsync
    /// — Blazor lo rellena tras el primer render del árbol de componentes. La
    /// apertura automática por query string (deep-link desde Alertas/Calendario/
    /// Dashboard/palette) tiene que esperar a este punto.
    /// </summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        if (DocumentoId is not null)
            await _drawerGestion.AbrirEditarAsync(DocumentoId.Value);
        else if (TrabajadorId is not null && TipoDocumentoId is not null)
            await _drawerGestion.AbrirCrearParaFaltanteAsync(TrabajadorId.Value, TipoDocumentoId.Value);
        else if (EmpresaIdFaltante is not null && TipoDocumentoId is not null)
            await _drawerGestion.AbrirCrearParaFaltanteEmpresaAsync(EmpresaIdFaltante.Value, TipoDocumentoId.Value);
        else if (Accion == "crear")
            await _drawerGestion.AbrirCrearAsync();
        else
            return;

        StateHasChanged();
    }

    private Task ManejarDocumentoGuardadoAsync() => RecargarAsync();

    /// <summary>
    /// Se re-ejecuta en cada navegación dentro de la propia página (recargar,
    /// compartir la URL, volver atrás) — no solo en el primer render — para
    /// que la URL sea la fuente de verdad de los tres filtros de la rejilla,
    /// no solo su semilla inicial (P1-18 de docs/business/MATURITY_REVIEW.md).
    /// </summary>
    protected override void OnParametersSet()
    {
        _estadoFiltro = !string.IsNullOrWhiteSpace(Estado) && Enum.TryParse<EstadoDocumento>(Estado, out _)
            ? Estado
            : string.Empty;
        _busqueda = TerminoBusquedaInicial ?? string.Empty;
        _ambitoFiltro = Ambito ?? string.Empty;

        // Deep-link de pestaña: lo usa el timeline de Comunicaciones para llevar
        // desde el evento de reclamación enviada a su pestaña. Se ignora un
        // valor que no exista en vez de dejar la página en blanco.
        if (!string.IsNullOrWhiteSpace(Pestana) && _pestanasDocumentos.Any(p => p.Id == Pestana))
            _pestanaActiva = Pestana;

        // A diferencia de "accion=crear" (OnAfterRenderAsync, solo primer
        // render: siempre llega desde otra página), "guardar-filtro" tiene
        // que funcionar estando YA en /documentos — el propio Command
        // Palette navega a la misma ruta añadiendo el query string, sin
        // recrear el componente. OnParametersSet es el único hook que se
        // re-ejecuta en ese caso, y se ejecuta después de resincronizar los
        // filtros de arriba desde la URL, así que el modal parte de los
        // filtros ya vigentes en pantalla.
        if (Accion == "guardar-filtro")
            _mostrarGuardarFiltro = true;
    }

    private readonly PaginationState _paginacion = new() { ItemsPerPage = 20 };

    // H2 (docs/ux-audit/02-clientes.md): paginador único en español, ver Clientes.razor.cs.
    private int TotalPaginas => Math.Max(1, (int)Math.Ceiling(_totalElementos / (double)_paginacion.ItemsPerPage));

    private Task CambiarPaginaAsync(int pagina) => _paginacion.SetCurrentPageIndexAsync(pagina - 1);

    // H5 (docs/ux-audit/05-trabajadores-vehiculos.md): selector de tamaño de página, compartido por PaginadorSimple.razor.
    private async Task CambiarTamanoPaginaAsync(int tamano)
    {
        _paginacion.ItemsPerPage = tamano;
        await _paginacion.SetCurrentPageIndexAsync(0);
        if (_grid is not null)
            await _grid.RefreshDataAsync();
    }

    private QuickGrid<DocumentoListaDto>? _grid;

    private string _busqueda = string.Empty;
    private string _ambitoFiltro = string.Empty;
    private string _estadoFiltro = string.Empty;
    private bool _cargando = true;
    private bool _errorCarga;
    private int _totalElementos;

    private bool _confirmarEliminarVisible;
    private Guid _idAEliminar;
    private string _propietarioAEliminar = string.Empty;
    private string _tipoDocumentoAEliminar = string.Empty;
    private bool _eliminando;

    private async ValueTask<GridItemsProviderResult<DocumentoListaDto>> ProveerElementosAsync(
        GridItemsProviderRequest<DocumentoListaDto> request)
    {
        _cargando = true;
        _errorCarga = false;

        try
        {
            var pagina = (request.StartIndex / _paginacion.ItemsPerPage) + 1;
            var (ordenarPor, descendente) = LecturaOrden.Leer(request);

            var ambitoFiltro = Enum.TryParse<AmbitoAplicacion>(_ambitoFiltro, out var ambito) ? ambito : (AmbitoAplicacion?)null;
            var estadoFiltro = Enum.TryParse<EstadoDocumento>(_estadoFiltro, out var estado) ? estado : (EstadoDocumento?)null;

            var resultado = await Mediator.Send(new ObtenerDocumentosQuery(
                TrabajadorId: null,
                Ambito: ambitoFiltro,
                Busqueda: string.IsNullOrWhiteSpace(_busqueda) ? null : _busqueda,
                Estado: estadoFiltro,
                Pagina: pagina,
                TamanoPagina: _paginacion.ItemsPerPage,
                OrdenarPor: ordenarPor,
                Descendente: descendente));

            _totalElementos = resultado.TotalElementos;

            var elementos = resultado.Elementos.ToList();
            _elementosPagina = elementos;
            _seleccionados.Clear();
            _idEnfocado = null;

            return GridItemsProviderResult.From(elementos, resultado.TotalElementos);
        }
        catch (Exception ex)
        {
            // _errorCarga solo pinta un aviso genérico en la rejilla; sin este
            // log, un fallo al cargar la pantalla más usada del producto no
            // deja ningún rastro que permita diagnosticarlo después.
            Logger.LogError(ex, "Error al cargar la rejilla de documentos (StartIndex {StartIndex}).", request.StartIndex);

            _errorCarga = true;
            return GridItemsProviderResult.From(new List<DocumentoListaDto>(), 0);
        }
        finally
        {
            _cargando = false;
            StateHasChanged();
        }
    }

    private async Task BuscarAsync(string valor)
    {
        _busqueda = valor;
        NavigationManager.ActualizarFiltroEnUrl("q", valor);
        await RecargarAsync();
    }

    private async Task CambiarAmbitoFiltroAsync(string valor)
    {
        _ambitoFiltro = valor;
        NavigationManager.ActualizarFiltroEnUrl(nameof(Ambito), valor);
        await RecargarAsync();
    }

    private async Task CambiarEstadoFiltroAsync(string valor)
    {
        _estadoFiltro = valor;
        NavigationManager.ActualizarFiltroEnUrl(nameof(Estado), valor);
        await RecargarAsync();
    }

    private async Task RecargarAsync()
    {
        await _paginacion.SetCurrentPageIndexAsync(0);

        if (_grid is not null)
            await _grid.RefreshDataAsync();

        StateHasChanged();
    }

    private void AbrirEliminar(Guid id, string propietarioNombre, string tipoDocumentoNombre)
    {
        _idAEliminar = id;
        _propietarioAEliminar = propietarioNombre;
        _tipoDocumentoAEliminar = tipoDocumentoNombre;
        _confirmarEliminarVisible = true;
    }

    private async Task ConfirmarEliminarAsync()
    {
        _eliminando = true;

        try
        {
            var resultado = await Mediator.Send(new EliminarDocumentoCommand(_idAEliminar));

            if (resultado.EsFallido)
            {
                ToastService.Mostrar(resultado.Error.Mensaje, TonoToast.Error);
            }
            else
            {
                var idEliminado = _idAEliminar;
                ToastService.Mostrar("Documento eliminado correctamente.", TonoToast.Exito, "Deshacer", () => DeshacerEliminarAsync(idEliminado));
                _confirmarEliminarVisible = false;
                await RecargarAsync();
            }
        }
        catch (Exception)
        {
            ToastService.Mostrar("No pudimos eliminar el documento. Intenta nuevamente en unos segundos.", TonoToast.Error);
        }
        finally
        {
            _eliminando = false;
        }
    }

    /// <summary>Fase D ("Deshacer al eliminar") — acción del toast tras eliminar, ver RestaurarDocumentoCommand.</summary>
    private async Task DeshacerEliminarAsync(Guid id)
    {
        var resultado = await Mediator.Send(new RestaurarDocumentoCommand(id));

        ToastService.Mostrar(
            resultado.EsExitoso ? "Documento restaurado." : resultado.Error.Mensaje,
            resultado.EsExitoso ? TonoToast.Exito : TonoToast.Error);

        if (resultado.EsExitoso)
            await RecargarAsync();
    }

    // --- P3-31: selección múltiple ---

    private bool TodosSeleccionados =>
        _elementosPagina.Count > 0 && _elementosPagina.All(e => _seleccionados.Contains(e.Id));

    private void AlternarSeleccionTodos(bool marcar)
    {
        if (marcar)
            foreach (var elemento in _elementosPagina) _seleccionados.Add(elemento.Id);
        else
            _seleccionados.Clear();
    }

    private void AlternarSeleccion(Guid id, bool marcado)
    {
        if (marcado) _seleccionados.Add(id);
        else _seleccionados.Remove(id);
    }

    private async Task ConfirmarEliminarLoteAsync()
    {
        _eliminandoLote = true;

        try
        {
            var resultado = await Mediator.Send(new EliminarDocumentosCommand(_seleccionados.ToList()));
            var dto = resultado.Valor;

            ToastService.Mostrar(
                dto.Errores.Count == 0
                    ? $"{dto.Eliminados} documento(s) eliminado(s)."
                    : $"{dto.Eliminados} eliminado(s). {dto.Errores.Count} no se pudieron borrar: {string.Join(" ", dto.Errores)}",
                dto.Errores.Count == 0 ? TonoToast.Exito : TonoToast.Advertencia);

            _seleccionados.Clear();
            _confirmarEliminarLoteVisible = false;
            await RecargarAsync();
        }
        catch (Exception)
        {
            ToastService.Mostrar("No pudimos eliminar los documentos seleccionados. Intenta nuevamente.", TonoToast.Error);
        }
        finally
        {
            _eliminandoLote = false;
        }
    }

    // --- P3-31: atajos de teclado j/k/x/Enter ---

    private string ObtenerClaseFila(DocumentoListaDto item) => item.Id == _idEnfocado ? "fila-enfocada" : "";

    private async Task ManejarAtajoAsync(string tecla)
    {
        if (_elementosPagina.Count == 0) return;

        switch (tecla)
        {
            case "j":
                {
                    var indiceActual = _idEnfocado is null ? -1 : _elementosPagina.FindIndex(e => e.Id == _idEnfocado);
                    _idEnfocado = _elementosPagina[Math.Min(indiceActual + 1, _elementosPagina.Count - 1)].Id;
                    break;
                }
            case "k":
                {
                    var indiceActual = _idEnfocado is null ? 0 : _elementosPagina.FindIndex(e => e.Id == _idEnfocado);
                    _idEnfocado = _elementosPagina[Math.Max(indiceActual - 1, 0)].Id;
                    break;
                }
            case "x":
                if (_idEnfocado is { } idAlternar)
                    AlternarSeleccion(idAlternar, !_seleccionados.Contains(idAlternar));
                break;
            case "Enter":
                if (_idEnfocado is { } idAbrir)
                {
                    var elemento = _elementosPagina.FirstOrDefault(e => e.Id == idAbrir);
                    if (elemento is not null)
                        await WorkspaceService.AbrirAsync(EntidadWorkspace.Documento, elemento.Id, elemento.TipoDocumentoNombre, "informacion");
                }
                break;
        }

        StateHasChanged();
    }

    // --- P3-31: filtros guardados ---

    private async Task AplicarFiltroGuardadoAsync(string idTexto)
    {
        if (!Guid.TryParse(idTexto, out var id)) return;

        var filtro = _filtrosGuardados.FirstOrDefault(f => f.Id == id);
        if (filtro is null) return;

        var valores = JsonSerializer.Deserialize<FiltrosDocumentosJson>(filtro.ValoresJson);
        if (valores is null) return;

        _busqueda = valores.Busqueda ?? string.Empty;
        _ambitoFiltro = valores.Ambito ?? string.Empty;
        _estadoFiltro = valores.Estado ?? string.Empty;
        await RecargarAsync();
    }

    private async Task GuardarFiltroActualAsync()
    {
        if (string.IsNullOrWhiteSpace(_nombreFiltroNuevo)) return;

        _guardandoFiltro = true;

        try
        {
            var valoresJson = JsonSerializer.Serialize(new FiltrosDocumentosJson(
                string.IsNullOrWhiteSpace(_busqueda) ? null : _busqueda,
                string.IsNullOrWhiteSpace(_ambitoFiltro) ? null : _ambitoFiltro,
                string.IsNullOrWhiteSpace(_estadoFiltro) ? null : _estadoFiltro));

            var resultado = await Mediator.Send(
                new GuardarFiltroCommand(PantallasConFiltrosGuardados.Documentos, _nombreFiltroNuevo, valoresJson));

            if (resultado.EsFallido)
            {
                ToastService.Mostrar(resultado.Error.Mensaje, TonoToast.Error);
                return;
            }

            _filtrosGuardados = await Mediator.Send(new ObtenerFiltrosGuardadosQuery(PantallasConFiltrosGuardados.Documentos));
            _mostrarGuardarFiltro = false;
            _nombreFiltroNuevo = string.Empty;
            ToastService.Mostrar("Filtro guardado.", TonoToast.Exito);
        }
        finally
        {
            _guardandoFiltro = false;
        }
    }

    private async Task EliminarFiltroGuardadoAsync(Guid id)
    {
        var resultado = await Mediator.Send(new EliminarFiltroGuardadoCommand(id));
        if (resultado.EsFallido)
        {
            ToastService.Mostrar(resultado.Error.Mensaje, TonoToast.Error);
            return;
        }

        _filtrosGuardados = await Mediator.Send(new ObtenerFiltrosGuardadosQuery(PantallasConFiltrosGuardados.Documentos));
    }
}
