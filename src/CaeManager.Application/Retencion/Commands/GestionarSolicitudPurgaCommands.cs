using CaeManager.Application.Common;
using CaeManager.Domain.Common;
using CaeManager.Domain.Retencion;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Options;

namespace CaeManager.Application.Retencion.Commands;

/// <summary>
/// Lanza el barrido que busca datos con el plazo cumplido y crea las
/// propuestas correspondientes.
///
/// <para>
/// Ya no es la única vía de detección: <c>RetencionHostedService</c> (REC-084)
/// hace lo mismo una vez al día por tenant, invocando directamente
/// <see cref="DeteccionPurgaService.DetectarAsync"/> sin pasar por este Command.
/// Este sigue siendo el disparador manual desde la pantalla de Retención.
/// </para>
///
/// <para>
/// Detectar es lo único automatizado: ni este Command ni el barrido programan
/// o ejecutan una purga: esos pasos siguen exigiendo una acción humana explícita.
/// </para>
/// </summary>
public record BuscarDatosPurgablesCommand : ICommand<int>;

public class BuscarDatosPurgablesCommandHandler(
    DeteccionPurgaService deteccion, IOptions<RetencionDatosOptions> opciones)
    : IRequestHandler<BuscarDatosPurgablesCommand, Result<int>>
{
    public async Task<Result<int>> Handle(BuscarDatosPurgablesCommand request, CancellationToken cancellationToken)
    {
        if (!opciones.Value.Activa)
            return Result.Fallo<int>(Error.Crear(
                "Retencion.Desactivada",
                "La política de retención está desactivada. Usa el diagnóstico para ver qué sería purgable, o actívala en la configuración para poder crear propuestas."));

        var creadas = await deteccion.DetectarAsync(DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);

        return Result.Exito(creadas);
    }
}

/// <summary>
/// El modo diagnóstico de DEC-35: cuenta lo purgable por categoría sin crear
/// ninguna <see cref="Domain.Retencion.SolicitudPurga"/>, así que —a
/// diferencia de <see cref="BuscarDatosPurgablesCommand"/>— no exige política
/// activa. Es el gemelo que invierte la guarda: sin política solo se puede
/// diagnosticar, nunca proponer ni destruir; con política, el camino sigue
/// siendo <see cref="BuscarDatosPurgablesCommand"/>.
/// </summary>
public record DiagnosticarDatosPurgablesCommand : ICommand<ResultadoDiagnosticoPurgaDto>;

public record ResultadoDiagnosticoPurgaDto(int DocumentosPurgables, int TrabajadoresPurgables, int Total);

public class DiagnosticarDatosPurgablesCommandHandler(DeteccionPurgaService deteccion)
    : IRequestHandler<DiagnosticarDatosPurgablesCommand, Result<ResultadoDiagnosticoPurgaDto>>
{
    public async Task<Result<ResultadoDiagnosticoPurgaDto>> Handle(
        DiagnosticarDatosPurgablesCommand request, CancellationToken cancellationToken)
    {
        var resultado = await deteccion.DiagnosticarAsync(DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);

        return Result.Exito(new ResultadoDiagnosticoPurgaDto(
            resultado.DocumentosPurgables, resultado.TrabajadoresPurgables, resultado.Total));
    }
}

/// <summary>Deja constancia de que se avisó al tenant antes de destruir nada.</summary>
public record MarcarTenantAvisadoCommand(Guid SolicitudId) : ICommand;

public class MarcarTenantAvisadoCommandHandler(ISolicitudPurgaRepository repositorio, IUnitOfWork unitOfWork)
    : IRequestHandler<MarcarTenantAvisadoCommand, Result>
{
    public async Task<Result> Handle(MarcarTenantAvisadoCommand request, CancellationToken cancellationToken)
    {
        var solicitud = await repositorio.ObtenerPorIdAsync(request.SolicitudId, cancellationToken);
        if (solicitud is null)
            return Result.Fallo(Error.Crear("SolicitudPurga.NoEncontrada", "No encontramos esa solicitud."));

        solicitud.MarcarTenantAvisado();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Exito();
    }
}

/// <summary>
/// Autoriza la destrucción y fija cuándo puede ejecutarse. Es el único camino
/// hacia la ejecución.
/// </summary>
public record ProgramarPurgaCommand(Guid SolicitudId, DateOnly FechaEjecucion) : ICommand;

public class ProgramarPurgaCommandValidator : AbstractValidator<ProgramarPurgaCommand>
{
    public ProgramarPurgaCommandValidator()
    {
        RuleFor(c => c.SolicitudId).NotEmpty();
    }
}

public class ProgramarPurgaCommandHandler(
    ISolicitudPurgaRepository repositorio, ICurrentUserService currentUserService, IUnitOfWork unitOfWork)
    : IRequestHandler<ProgramarPurgaCommand, Result>
{
    public async Task<Result> Handle(ProgramarPurgaCommand request, CancellationToken cancellationToken)
    {
        var solicitud = await repositorio.ObtenerPorIdAsync(request.SolicitudId, cancellationToken);
        if (solicitud is null)
            return Result.Fallo(Error.Crear("SolicitudPurga.NoEncontrada", "No encontramos esa solicitud."));

        var usuarioId = await currentUserService.ObtenerUsuarioActualIdAsync();
        if (usuarioId is null)
            return Result.Fallo(Error.Crear("Retencion.SinUsuario", "No pudimos identificarte. Vuelve a iniciar sesión."));

        try
        {
            // La atribución no es un adorno: destruir datos personales tiene
            // que quedar imputado a una persona concreta.
            solicitud.Programar(request.FechaEjecucion, usuarioId.Value, DateOnly.FromDateTime(DateTime.UtcNow));
        }
        catch (ArgumentException ex)
        {
            return Result.Fallo(Error.Crear("SolicitudPurga.FechaNoValida", ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Result.Fallo(Error.Crear("SolicitudPurga.EstadoNoValido", ex.Message));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Exito();
    }
}

/// <summary>Descarta la propuesta — el caso del tenant que pide conservar sus datos más tiempo.</summary>
public record CancelarPurgaCommand(Guid SolicitudId, string Motivo) : ICommand;

public class CancelarPurgaCommandValidator : AbstractValidator<CancelarPurgaCommand>
{
    public CancelarPurgaCommandValidator()
    {
        RuleFor(c => c.SolicitudId).NotEmpty();
        RuleFor(c => c.Motivo)
            .NotEmpty().WithMessage("Indica por qué se descarta esta purga.")
            .MaximumLength(SolicitudPurga.LongitudMaximaMotivo);
    }
}

public class CancelarPurgaCommandHandler(ISolicitudPurgaRepository repositorio, IUnitOfWork unitOfWork)
    : IRequestHandler<CancelarPurgaCommand, Result>
{
    public async Task<Result> Handle(CancelarPurgaCommand request, CancellationToken cancellationToken)
    {
        var solicitud = await repositorio.ObtenerPorIdAsync(request.SolicitudId, cancellationToken);
        if (solicitud is null)
            return Result.Fallo(Error.Crear("SolicitudPurga.NoEncontrada", "No encontramos esa solicitud."));

        try
        {
            solicitud.Cancelar(request.Motivo);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Fallo(Error.Crear("SolicitudPurga.EstadoNoValido", ex.Message));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Exito();
    }
}

/// <summary>
/// Ejecuta una purga cuya fecha ya llegó. Sigue siendo una acción explícita
/// aunque esté autorizada: es irreversible, y que la fecha llegue no es razón
/// para que ocurra sin que nadie mire.
/// </summary>
public record EjecutarPurgaCommand(Guid SolicitudId) : ICommand<int>;

public class EjecutarPurgaCommandHandler(EjecucionPurgaService ejecucion)
    : IRequestHandler<EjecutarPurgaCommand, Result<int>>
{
    public async Task<Result<int>> Handle(EjecutarPurgaCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var afectados = await ejecucion.EjecutarAsync(
                request.SolicitudId, DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);

            return Result.Exito(afectados);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Fallo<int>(Error.Crear("SolicitudPurga.NoEjecutable", ex.Message));
        }
    }
}
