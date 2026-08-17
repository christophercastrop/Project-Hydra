using CaeManager.Application.Alertas;
using CaeManager.Application.Asignaciones.Commands.CrearAsignacion;
using CaeManager.Application.Asignaciones.Commands.CrearAsignaciones;
using CaeManager.Application.Asignaciones.Commands.DarDeBajaAsignaciones;
using CaeManager.Application.Asignaciones.Queries.ObtenerAsignacionesDocumentacionPorCentro;
using CaeManager.Application.Asignaciones.Queries.ObtenerDocumentosFaltantesParaAsignacion;
using CaeManager.Application.Asignaciones.Queries.ObtenerTrabajadoresVisitaSinAsignacion;
using CaeManager.Application.Centros.Queries.ObtenerCentros;
using CaeManager.Web.Components.Workspace;
using CaeManager.Application.Centros.Queries.ObtenerCentrosParaSelector;
using CaeManager.Application.Trabajadores.Queries.ObtenerTrabajadoresParaSelector;
using CaeManager.Domain.Documentos;
using CaeManager.Web.Components;
using CaeManager.Web.Components.DesignSystem;
using CaeManager.Web.Features.Documentos.Components;
using FluentValidation;
using Microsoft.AspNetCore.Components;

namespace CaeManager.Web.Features.Centros.Components;

public partial class AcordeonAsignacionesCentro : ComponentBase
{
    [Inject] private ContextWorkspaceService WorkspaceService { get; set; } = default!;

    [Parameter, EditorRequired] public Guid CentroId { get; set; }
    [Parameter, EditorRequired] public string CentroNombre { get; set; } = string.Empty;

    /// <summary>Próxima visita activa del centro (Centros.razor la resuelve en lote) — null si no tiene ninguna.</summary>
    [Parameter] public Guid? VisitaId { get; set; }
    [Parameter] public DateOnly? VisitaFechaFin { get; set; }

    /// <summary>Empresa titular del centro, sujeto del ámbito Empresa (OD-13).</summary>
    [Parameter] public Guid EmpresaId { get; set; }
    [Parameter] public string EmpresaNombre { get; set; } = string.Empty;

    /// <summary>
    /// Incidencias del ámbito Empresa, ya calculadas por la fila de Centro. No
    /// se vuelven a consultar: son las mismas causas que decidieron el estado
    /// del centro, filtradas por ámbito. El acordeón solo las presenta.
    /// </summary>
    [Parameter] public IReadOnlyList<IncidenciaCentroDto> IncidenciasEmpresa { get; set; } = [];

    /// <summary>
    /// Se dispara tras guardar un documento o una asignación in situ —
    /// Centros.razor vuelve a pedir esta fila (ver RefrescarCentroAsync).
    /// Un nombre solo, no uno por tipo de guardado: al padre le basta con
    /// saber "algo de este centro cambió", no distinguir la causa.
    /// </summary>
    [Parameter] public EventCallback OnCambio { get; set; }

    /// <summary>
    /// "Selección múltiple" de la página (Centros.razor) — los checkboxes de
    /// fila de Trabajador solo se pintan con esto activo, mismo criterio que
    /// ya aplica la fila de Centro (§ 0.9): son ruido permanente para una
    /// acción ocasional, no algo "de serie".
    /// </summary>
    [Parameter] public bool SeleccionMultiple { get; set; }

    /// <summary>
    /// Vista previa para la fila de /centros (hallazgo en vivo 2026-08-16):
    /// con un centro de plantilla grande, desplegar TODOS los trabajadores
    /// dentro de la lista convertía la fila en una lista interminable — peor
    /// aún si además se abre la tabla de documentos de cada uno. Activo,
    /// solo se listan los trabajadores con alguna incidencia (PeorEstado
    /// distinto de Vigente); Centro 360 (la página propia, mockup "Centro
    /// 360 TALVEG") sigue mostrando la lista completa de trabajadores con
    /// actividad, incidencia o no — es la que conserva los tres niveles sin
    /// recortar.
    /// </summary>
    [Parameter] public bool SoloIncidencias { get; set; }

    [Inject] private NavigationManager NavigationManager { get; set; } = default!;

    private bool _cargando = true;
    private bool _errorCarga;
    private IReadOnlyList<TrabajadorAsignacionDocumentacionDto> _trabajadores = [];
    private string _busquedaTrabajador = string.Empty;
    private readonly HashSet<Guid> _seleccionados = [];
    private bool _seleccionMultipleAnterior;

    /// <summary>
    /// Qué filas de Trabajador tienen su tabla de documentos abierta (variante
    /// A, OD-12 cerrada). Estado propio del componente, no de
    /// <c>SeccionColapsable</c> (contrato de fidelidad 2026-08-09 § 1.2/1.4):
    /// esa composición queda prohibida como base visual de esta fila, aunque
    /// el mecanismo de expandir/colapsar en sí — el mismo bool toggle — sí se
    /// reutiliza, igual que ya hace la fila de Centro con su propio HashSet.
    /// </summary>
    private readonly HashSet<Guid> _expandidosTrabajador = [];

    private IReadOnlyList<TrabajadorSinAsignacionDto> _trabajadoresVisitaSinAsignacion = [];
    private readonly HashSet<Guid> _asignandoDesdeVisita = [];

    private bool _confirmarBajaLoteVisible;
    private string _fechaBajaLote = string.Empty;
    private bool _procesandoBajaLote;

    private DrawerAsignacionMasiva _drawerAsignacion = default!;

    protected override Task OnInitializedAsync() => CargarAsync();

    /// <summary>Apagar "Selección múltiple" limpia la selección de trabajadores — mismo criterio que Centros.razor.cs.AlternarSeleccionMultiple.</summary>
    protected override void OnParametersSet()
    {
        if (_seleccionMultipleAnterior && !SeleccionMultiple)
            _seleccionados.Clear();
        _seleccionMultipleAnterior = SeleccionMultiple;
    }

    private async Task CargarAsync()
    {
        _cargando = true;
        _errorCarga = false;
        StateHasChanged();

        try
        {
            _trabajadores = await Mediator.Send(new ObtenerAsignacionesDocumentacionPorCentroQuery(CentroId, VisitaFechaFin));
            _seleccionados.Clear();

            _trabajadoresVisitaSinAsignacion = VisitaId is { } visitaId
                ? await Mediator.Send(new ObtenerTrabajadoresVisitaSinAsignacionQuery(visitaId, CentroId))
                : [];
        }
        catch (Exception)
        {
            _errorCarga = true;
        }
        finally
        {
            _cargando = false;
            StateHasChanged();
        }
    }

    /// <summary>
    /// "Asignación rápida desde visita" (§ 0.3): el trabajador ya está
    /// identificado por <c>VisitaTrabajador</c> — no hace falta un selector,
    /// solo confirmar la fecha de alta (hoy) y avisar si le faltará algún
    /// documento obligatorio, mismo preflight no bloqueante que el drawer N×M.
    /// </summary>
    private async Task AsignarDesdeVisitaAsync(TrabajadorSinAsignacionDto trabajador)
    {
        _asignandoDesdeVisita.Add(trabajador.TrabajadorId);
        StateHasChanged();

        try
        {
            var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
            var faltantes = await Mediator.Send(new ObtenerDocumentosFaltantesParaAsignacionQuery([trabajador.TrabajadorId], [CentroId]));

            var resultado = await Mediator.Send(new CrearAsignacionCommand(trabajador.TrabajadorId, CentroId, hoy));
            if (resultado.EsFallido)
            {
                ToastService.Mostrar(resultado.Error.Mensaje, TonoToast.Error);
                return;
            }

            ToastService.Mostrar(
                faltantes.Count == 0
                    ? $"{trabajador.TrabajadorNombre} asignado a {CentroNombre}."
                    : $"{trabajador.TrabajadorNombre} asignado a {CentroNombre} — le faltan {faltantes.Count} documento(s) obligatorio(s).",
                faltantes.Count == 0 ? TonoToast.Exito : TonoToast.Advertencia);

            await CargarAsync();
        }
        catch (Exception)
        {
            ToastService.Mostrar("No pudimos asignar al trabajador. Intenta nuevamente en unos segundos.", TonoToast.Error);
        }
        finally
        {
            _asignandoDesdeVisita.Remove(trabajador.TrabajadorId);
        }
    }

    /// <summary>"7/9" junto al nombre (PLAN-EJECUCION-UX.md § 0.5) — se deriva de los mismos <c>Documentos</c> ya cargados, sin consulta nueva.</summary>
    private static int DocumentosAlDia(TrabajadorAsignacionDocumentacionDto trabajador) =>
        trabajador.Documentos.Count(d => d.Estado == EstadoDocumento.Vigente);

    /// <summary>Lista realmente pintada — recortada a incidencias en la vista previa de /centros, completa en Centro 360.</summary>
    private IReadOnlyList<TrabajadorAsignacionDocumentacionDto> TrabajadoresAMostrar =>
        SoloIncidencias ? _trabajadores.Where(t => t.PeorEstado != EstadoDocumento.Vigente).ToList() : _trabajadores;

    private int TrabajadoresOcultosAlDia => SoloIncidencias ? _trabajadores.Count - TrabajadoresAMostrar.Count : 0;

    /// <summary>
    /// Búsqueda por nombre sobre <see cref="TrabajadoresAMostrar"/> — client-side,
    /// sin consulta nueva: con una plantilla grande (Centro 360, SoloIncidencias=false)
    /// la lista completa no tenía ninguna forma de encontrar un trabajador
    /// concreto sin desplazarse a mano.
    /// </summary>
    private IReadOnlyList<TrabajadorAsignacionDocumentacionDto> TrabajadoresFiltrados =>
        string.IsNullOrWhiteSpace(_busquedaTrabajador)
            ? TrabajadoresAMostrar
            : TrabajadoresAMostrar.Where(t => t.TrabajadorNombre.Contains(_busquedaTrabajador, StringComparison.OrdinalIgnoreCase)).ToList();

    private void VerCentroCompleto() => NavigationManager.NavigateTo($"/centros/{CentroId}");

    private void AlternarExpansionTrabajador(Guid asignacionId)
    {
        if (!_expandidosTrabajador.Add(asignacionId))
            _expandidosTrabajador.Remove(asignacionId);
    }

    /// <summary>
    /// Documentos vencidos del trabajador para la 1ª ranura de recuento
    /// (contrato de fidelidad 2026-08-09 § 1.1/1.4). "Faltante cuenta como
    /// vencido" — mismo criterio que <c>ObtenerCentrosQuery.Desglosar</c> a
    /// nivel de Centro, para que el léxico cerrado no necesite una tercera
    /// casilla también aquí.
    /// </summary>
    private static IReadOnlyList<DocumentoRequeridoDto> DocumentosVencidos(TrabajadorAsignacionDocumentacionDto trabajador) =>
        trabajador.Documentos.Where(d => d.Estado is EstadoDocumento.Vencido or EstadoDocumento.Faltante).ToList();

    /// <summary>Documentos próximos a vencer del trabajador, para la 2ª ranura de recuento.</summary>
    private static IReadOnlyList<DocumentoRequeridoDto> DocumentosProximos(TrabajadorAsignacionDocumentacionDto trabajador) =>
        trabajador.Documentos.Where(d => d.Estado == EstadoDocumento.Proximo).ToList();

    /// <summary>
    /// Documentos en ventana urgente del trabajador, para la 3ª ranura.
    /// "Urgente" es la única severidad que ni la 1ª ni la 2ª ranura recogen
    /// (mismo criterio que <c>ObtenerCentrosQuery.Desglosar</c>, que tampoco
    /// la cuenta en ninguno de los dos recuentos del Centro) — por eso es lo
    /// único que le queda por decir a esta ranura sin repetir lo que ya dicen
    /// las dos anteriores. Mostrar aquí <c>trabajador.PeorEstado</c> siempre
    /// (como hacía la cabecera de <c>SeccionColapsable</c>) duplicaba el
    /// recuento de vencidos/próximos con otro badge al lado — justo el efecto
    /// que la lámina evita dejando esta ranura vacía en la mayoría de filas.
    /// </summary>
    private static IReadOnlyList<DocumentoRequeridoDto> DocumentosUrgentes(TrabajadorAsignacionDocumentacionDto trabajador) =>
        trabajador.Documentos.Where(d => d.Estado == EstadoDocumento.Urgente).ToList();

    /// <summary>Nombre accesible del badge de solo recuento — mismo criterio que <c>Centros.razor.cs.DescribirRecuento</c>, sin el desglose por ámbito porque aquí el sujeto ya es un único trabajador.</summary>
    private static string DescribirRecuentoTrabajador(IReadOnlyList<DocumentoRequeridoDto> documentos, string calificativo) =>
        documentos.Count == 1
            ? $"1 documento {calificativo}"
            : $"{documentos.Count} documentos {(calificativo.EndsWith('o') ? calificativo + "s" : calificativo)}";

    private static string DescribirDocumentoIncidencia(DocumentoRequeridoDto documento) => documento.Estado switch
    {
        EstadoDocumento.Faltante => $"{documento.TipoDocumentoNombre} — pendiente de subir",
        EstadoDocumento.Vencido when documento.FechaVencimiento is { } vencio => $"{documento.TipoDocumentoNombre} — venció {vencio:dd/MM/yyyy}",
        EstadoDocumento.Proximo when documento.FechaVencimiento is { } caduca => $"{documento.TipoDocumentoNombre} — caduca {caduca:dd/MM/yyyy}",
        _ => documento.TipoDocumentoNombre
    };

    /// <summary>"Detalles" de la fila de Trabajador (blueprint § 3.3, contrato § 1.4) — mismo patrón que Trabajadores.razor/Incidencias.razor/Gestiones.razor, no una ruta nueva.</summary>
    private Task AbrirDetalleTrabajador(TrabajadorAsignacionDocumentacionDto trabajador) =>
        WorkspaceService.AbrirAsync(EntidadWorkspace.Trabajador, trabajador.TrabajadorId, trabajador.TrabajadorNombre, "informacion");

    private void AlternarSeleccion(Guid asignacionId, bool marcado)
    {
        if (marcado) _seleccionados.Add(asignacionId);
        else _seleccionados.Remove(asignacionId);
    }

    private DrawerGestionDocumento _drawerGestion = default!;

    /// <summary>
    /// Gestionar in situ (contrato de fidelidad 2026-08-09, item 3 del
    /// backlog): antes navegaba a /documentos, ahora abre el mismo drawer
    /// compartido sin salir de Centro 360. Un documento faltante no tiene
    /// DocumentoId todavía — abre el drawer de creación con el propietario y
    /// el tipo ya elegidos, mismo patrón que "Gestionar" en Alertas.razor.cs.
    /// </summary>
    private Task GestionarAsync(Guid trabajadorId, DocumentoRequeridoDto documento) =>
        documento.DocumentoId is { } documentoId
            ? _drawerGestion.AbrirEditarAsync(documentoId)
            : _drawerGestion.AbrirCrearParaFaltanteAsync(trabajadorId, documento.TipoDocumentoId);

    /// <summary>
    /// Mismo patrón que <see cref="GestionarAsync"/> — hoy siempre resuelve a
    /// la rama con DocumentoId (AgregarCausasDeEmpresaAsync no detecta falta
    /// total), la otra rama queda lista para cuando ese alcance se amplíe.
    /// </summary>
    private Task GestionarEmpresaAsync(IncidenciaCentroDto incidencia) =>
        incidencia.DocumentoId is { } documentoId
            ? _drawerGestion.AbrirEditarAsync(documentoId)
            : _drawerGestion.AbrirCrearParaFaltanteEmpresaAsync(EmpresaId, incidencia.TipoDocumentoId!.Value);

    /// <summary>
    /// El bloque Empresa (IncidenciasEmpresa) y el badge/estado de la fila de
    /// Centro llegan como Parameter desde Centros.razor — este componente no
    /// los puede refrescar por sí solo, así que además de recargar su propia
    /// lista de Trabajador avisa al padre (OnCambio) para que vuelva a pedir
    /// la fila del Centro. Misma reacción tras guardar un documento o una
    /// asignación: los dos cambian el estado calculado del Centro.
    /// </summary>
    private async Task ManejarDocumentoGuardadoAsync()
    {
        await CargarAsync();
        if (OnCambio.HasDelegate)
            await OnCambio.InvokeAsync();
    }

    private Task ManejarAsignacionGuardadaAsync() => ManejarDocumentoGuardadoAsync();

    private static string TextoVigenciaEmpresa(IncidenciaCentroDto incidencia)
    {
        if (incidencia.FechaVencimiento is not { } fecha)
            return "Sin caducidad";

        var texto = fecha.ToString("dd/MM/yyyy");
        return incidencia.Estado == EstadoDocumento.Vencido ? $"Vencio {texto}" : $"Caduca {texto}";
    }

    private void AbrirConfirmarBajaLoteAsync()
    {
        _fechaBajaLote = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        _confirmarBajaLoteVisible = true;
    }

    private async Task ConfirmarBajaLoteAsync()
    {
        _procesandoBajaLote = true;

        try
        {
            if (!DateOnly.TryParse(_fechaBajaLote, out var fechaBaja))
            {
                ToastService.Mostrar("Introduce una fecha de baja válida.", TonoToast.Error);
                return;
            }

            var resultado = await Mediator.Send(new DarDeBajaAsignacionesCommand(_seleccionados.ToList(), fechaBaja));

            if (resultado.EsFallido)
            {
                ToastService.Mostrar(resultado.Error.Mensaje, TonoToast.Error);
                return;
            }

            var dto = resultado.Valor;
            ToastService.Mostrar(
                dto.Errores.Count == 0
                    ? $"{dto.DadasDeBaja} trabajador(es) dado(s) de baja."
                    : $"{dto.DadasDeBaja} dado(s) de baja. {dto.Errores.Count} no se pudieron procesar: {string.Join(" ", dto.Errores)}",
                dto.Errores.Count == 0 ? TonoToast.Exito : TonoToast.Advertencia);

            _confirmarBajaLoteVisible = false;
            await CargarAsync();
        }
        catch (Exception)
        {
            ToastService.Mostrar("No pudimos procesar la baja. Intenta nuevamente en unos segundos.", TonoToast.Error);
        }
        finally
        {
            _procesandoBajaLote = false;
        }
    }

    /// <summary>
    /// Abre el Context Panel de la Empresa. Es el destino que declara la nota
    /// del bloque: aqui solo se listan incidencias, la documentacion completa
    /// de la empresa se consulta en su propia ficha (blueprint seccion 3.3).
    /// </summary>
    private Task AbrirDetalleEmpresa() =>
        WorkspaceService.AbrirAsync(EntidadWorkspace.Empresa, EmpresaId, EmpresaNombre, "documentacion");

    /// <summary>
    /// Columna "Vigencia" del tercer nivel (blueprint § 3.4). El verbo cambia
    /// segun el estado porque una fecha suelta no dice si ya paso o esta por
    /// llegar, y esa es justo la pregunta del gestor.
    /// </summary>
    private static string TextoVigencia(DocumentoRequeridoDto documento)
    {
        if (documento.DocumentoId is null)
        {
            return "—";
        }

        if (documento.FechaVencimiento is not { } fecha)
        {
            // Documento sin caducidad: no es un hueco de datos, es una
            // propiedad del tipo documental. Se declara en vez de dejar "—",
            // que se leeria como "falta el dato".
            return "Sin caducidad";
        }

        var texto = fecha.ToString("dd/MM/yyyy");
        return documento.Estado == EstadoDocumento.Vencido ? $"Vencio {texto}" : $"Caduca {texto}";
    }

    /// <summary>
    /// Si la accion necesita peso visual. Un documento al dia conserva
    /// "Gestionar" pero atenuado (04 section 2.5): sigue disponible, deja de
    /// competir por la atencion con las filas que si piden intervencion.
    /// </summary>
    private static bool RequiereIntervencion(DocumentoRequeridoDto documento) =>
        documento.DocumentoId is null
        || documento.CaducaEnVentanaVisita
        || documento.Estado is EstadoDocumento.Vencido or EstadoDocumento.Proximo or EstadoDocumento.Faltante;
}
