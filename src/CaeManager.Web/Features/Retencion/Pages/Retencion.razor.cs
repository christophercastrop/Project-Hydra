using CaeManager.Application.Retencion.Commands;
using CaeManager.Application.Retencion.Queries;
using CaeManager.Domain.Retencion;
using CaeManager.Web.Components.DesignSystem;
using MediatR;
using Microsoft.AspNetCore.Components;

namespace CaeManager.Web.Features.Retencion.Pages;

/// <summary>
/// Revisión de las propuestas de purga (RGPD-TRATAMIENTO-DATOS.md § 5).
///
/// El orden de la pantalla refleja el procedimiento acordado con el
/// propietario del producto: detectar → avisar a la organización → autorizar
/// con fecha → ejecutar.
///
/// <para>
/// La detección (paso 1) ocurre de dos formas: el botón de esta pantalla, y el
/// barrido diario de <c>RetencionHostedService</c> (REC-084), que crea las
/// mismas propuestas sin que nadie las pida. Los tres pasos siguientes siguen
/// siendo íntegramente humanos: la máquina propone, la persona dispone, y hasta
/// el último momento se puede descartar. Ninguna rama automática programa ni
/// ejecuta una purga.
/// </para>
/// </summary>
public partial class Retencion : CaeManager.Web.Components.PaginaIntegrableConfiguracionBase
{
    [Inject] private IMediator Mediator { get; set; } = default!;
    [Inject] private ToastService ToastService { get; set; } = default!;
    [Inject] private ILogger<Retencion> Logger { get; set; } = default!;

    private IReadOnlyList<SolicitudPurgaDto> _solicitudes = [];
    private bool _cargando = true;
    private bool _error;
    private bool _buscando;
    private bool _procesando;
    private string? _errorFormulario;

    private SolicitudPurgaDto? _aProgramar;
    private SolicitudPurgaDto? _aCancelar;
    private SolicitudPurgaDto? _aEjecutar;

    private string _fechaEjecucion = string.Empty;
    private string _motivoCancelacion = string.Empty;

    protected override Task OnInitializedAsync() => CargarAsync();

    private async Task CargarAsync()
    {
        _cargando = true;
        _error = false;
        StateHasChanged();

        try
        {
            _solicitudes = await Mediator.Send(new ObtenerSolicitudesPurgaQuery());
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error al cargar las solicitudes de purga.");
            _error = true;
        }
        finally
        {
            _cargando = false;
        }
    }

    private async Task BuscarAsync()
    {
        _buscando = true;
        StateHasChanged();

        try
        {
            var resultado = await Mediator.Send(new BuscarDatosPurgablesCommand());

            if (resultado.EsFallido)
            {
                ToastService.Mostrar(resultado.Error.Mensaje, TonoToast.Error);
                return;
            }

            ToastService.Mostrar(
                resultado.Valor == 0
                    ? "Ningún dato ha cumplido todavía su plazo."
                    : $"{resultado.Valor} propuesta(s) nueva(s) pendientes de revisión.",
                TonoToast.Exito);

            await CargarAsync();
        }
        finally
        {
            _buscando = false;
        }
    }

    private async Task MarcarAvisadoAsync(SolicitudPurgaDto solicitud)
    {
        var resultado = await Mediator.Send(new MarcarTenantAvisadoCommand(solicitud.Id));

        if (resultado.EsFallido)
        {
            ToastService.Mostrar(resultado.Error.Mensaje, TonoToast.Error);
            return;
        }

        ToastService.Mostrar("Anotado que se avisó a la organización.", TonoToast.Exito);
        await CargarAsync();
    }

    private void AbrirProgramar(SolicitudPurgaDto solicitud)
    {
        _aProgramar = solicitud;
        // Un mes por defecto: margen suficiente para que la organización
        // reaccione, sin que la propuesta se quede olvidada indefinidamente.
        _fechaEjecucion = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd");
        _errorFormulario = null;
    }

    private void AbrirCancelar(SolicitudPurgaDto solicitud)
    {
        _aCancelar = solicitud;
        _motivoCancelacion = string.Empty;
        _errorFormulario = null;
    }

    private void AbrirEjecutar(SolicitudPurgaDto solicitud)
    {
        _aEjecutar = solicitud;
        _errorFormulario = null;
    }

    private async Task ProgramarAsync()
    {
        if (_aProgramar is null) return;

        _procesando = true;
        _errorFormulario = null;
        StateHasChanged();

        try
        {
            if (!DateOnly.TryParse(_fechaEjecucion, out var fecha))
            {
                _errorFormulario = "Indica una fecha válida.";
                return;
            }

            var resultado = await Mediator.Send(new ProgramarPurgaCommand(_aProgramar.Id, fecha));

            if (resultado.EsFallido)
            {
                _errorFormulario = resultado.Error.Mensaje;
                return;
            }

            ToastService.Mostrar("Purga autorizada.", TonoToast.Exito);
            _aProgramar = null;
            await CargarAsync();
        }
        finally
        {
            _procesando = false;
        }
    }

    private async Task CancelarAsync()
    {
        if (_aCancelar is null) return;

        _procesando = true;
        _errorFormulario = null;
        StateHasChanged();

        try
        {
            var resultado = await Mediator.Send(new CancelarPurgaCommand(_aCancelar.Id, _motivoCancelacion));

            if (resultado.EsFallido)
            {
                _errorFormulario = resultado.Error.Mensaje;
                return;
            }

            ToastService.Mostrar("Propuesta descartada.", TonoToast.Exito);
            _aCancelar = null;
            await CargarAsync();
        }
        catch (FluentValidation.ValidationException ex)
        {
            _errorFormulario = string.Join(" ", ex.Errors.Select(e => e.ErrorMessage));
        }
        finally
        {
            _procesando = false;
        }
    }

    private async Task EjecutarAsync()
    {
        if (_aEjecutar is null) return;

        _procesando = true;
        StateHasChanged();

        try
        {
            var resultado = await Mediator.Send(new EjecutarPurgaCommand(_aEjecutar.Id));

            if (resultado.EsFallido)
            {
                ToastService.Mostrar(resultado.Error.Mensaje, TonoToast.Error);
                return;
            }

            ToastService.Mostrar($"{resultado.Valor} registro(s) anonimizados.", TonoToast.Exito);
            _aEjecutar = null;
            await CargarAsync();
        }
        finally
        {
            _procesando = false;
        }
    }

    private static string DescribirTipo(TipoDatoPurgable tipo) => tipo switch
    {
        TipoDatoPurgable.Documentos => "Documentos",
        TipoDatoPurgable.TrabajadoresDadosDeBaja => "Trabajadores dados de baja",
        _ => tipo.ToString()
    };

    private static string DescribirEstado(SolicitudPurgaDto solicitud) => solicitud.Estado switch
    {
        EstadoSolicitudPurga.PendienteDeRevision => "Pendiente de revisar",
        EstadoSolicitudPurga.TenantAvisado => "Organización avisada",
        EstadoSolicitudPurga.Programada =>
            solicitud.PuedeEjecutarseHoy
                ? "Lista para ejecutar"
                : $"Autorizada para el {solicitud.FechaEjecucionProgramada:dd/MM/yyyy}",
        EstadoSolicitudPurga.Ejecutada => $"Ejecutada el {solicitud.EjecutadaEnUtc?.ToLocalTime():dd/MM/yyyy}",
        EstadoSolicitudPurga.Cancelada => "Descartada",
        _ => solicitud.Estado.ToString()
    };

    private static TonoBadge TonoDeEstado(EstadoSolicitudPurga estado) => estado switch
    {
        EstadoSolicitudPurga.PendienteDeRevision => TonoBadge.Advertencia,
        EstadoSolicitudPurga.TenantAvisado => TonoBadge.Advertencia,
        EstadoSolicitudPurga.Programada => TonoBadge.Peligro,
        EstadoSolicitudPurga.Ejecutada => TonoBadge.Neutro,
        _ => TonoBadge.Neutro
    };
}
