using CaeManager.Application.Asignaciones;
using CaeManager.Application.Clientes;
using CaeManager.Application.Common;
using CaeManager.Application.Documentos;
using CaeManager.Application.Empresas;
using CaeManager.Application.TiposDocumento;
using CaeManager.Application.Trabajadores;
using CaeManager.Domain.Documentos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CaeManager.Application.Centros.Queries.ObtenerDocumentacionBloqueantePendiente;

/// <summary>
/// Fase C (Bandeja del gestor): un Trabajador con Asignación activa a un
/// Centro donde falta un Documento Vigente de un TipoDocumento marcado
/// <c>TipoDocumentoCentro.BloqueaAcceso</c> (PLAN-EJECUCION-UX.md § 0.4) —
/// sustituye a ObtenerRequisitosDocumentalesPendientesQuery/RequisitoDocumental
/// (retirados): antes era un check manual a nivel de Centro
/// (BloqueaAcceso+Cumplido sin trabajador asociado), ahora es automático y
/// por trabajador, igual criterio que <c>CalculoEstadoCentroService</c>
/// (misma fuente: solo los huecos que además fuerzan <c>EstadoCentro.Bloqueado</c>,
/// porque son los únicos con urgencia real para una cola priorizada).
/// </summary>
public record ObtenerDocumentacionBloqueantePendienteQuery : IRequest<IReadOnlyList<DocumentacionBloqueantePendienteDto>>;

/// <param name="ClienteId">Cliente del Centro — alimenta la agrupación "por situación" del rediseño de Inicio (hallazgo P-03 de la auditoría de producto 2026-08-16). El Centro ya es exacto aquí, así que no hace falta ningún criterio de desambiguación.</param>
/// <param name="EmpresaId">Empresa del Trabajador — sub-agrupación Empresa→Trabajador de "Requiere atención" en vocabulario Consultora (GrupoCola).</param>
/// <param name="EsAltaNueva">
/// True cuando el Trabajador no tiene NINGÚN documento vigente de los tipos
/// que bloquean acceso en este Centro — nunca llegó a completar el alta, no
/// es que se le haya caducado uno. Señal sin umbral (decisión de producto
/// 2026-08-16): no depende de FechaAlta/CreadoEnUtc, solo de si ya hay algo
/// vigente o no. Distingue "sigue de alta" (visita tradicional, algo ya
/// vigente) de "nunca llegó a entrar" (alta nueva) para dar un tratamiento de
/// UI distinto — ver TipoItemBandejaUi.
/// </param>
public record DocumentacionBloqueantePendienteDto(
    Guid CentroId, string CentroNombre, Guid TrabajadorId, string TrabajadorNombre,
    Guid TipoDocumentoId, string TipoDocumentoNombre,
    Guid? ClienteId = null, string? ClienteNombre = null,
    Guid? EmpresaId = null, string? EmpresaNombre = null,
    bool EsAltaNueva = false);

public class ObtenerDocumentacionBloqueantePendienteQueryHandler(
    ICentrosQueryContext centrosContext,
    ITiposDocumentoQueryContext tiposDocumentoContext,
    ITrabajadoresQueryContext trabajadoresContext,
    IAsignacionesQueryContext asignacionesContext,
    IDocumentosQueryContext documentosContext,
    IClientesQueryContext clientesContext,
    IEmpresasQueryContext empresasContext,
    IAlcanceDatosService alcanceDatos)
    : IRequestHandler<ObtenerDocumentacionBloqueantePendienteQuery, IReadOnlyList<DocumentacionBloqueantePendienteDto>>
{
    public async Task<IReadOnlyList<DocumentacionBloqueantePendienteDto>> Handle(
        ObtenerDocumentacionBloqueantePendienteQuery request, CancellationToken cancellationToken)
    {
        var filasBloqueantes = await tiposDocumentoContext.TiposDocumentoCentros
            .Where(tc => tc.Incluido && tc.BloqueaAcceso)
            .Select(tc => new { tc.TipoDocumentoId, tc.CentroId })
            .ToListAsync(cancellationToken);

        if (filasBloqueantes.Count == 0)
            return [];

        var centroIdsVisibles = await alcanceDatos.ObtenerCentroIdsVisiblesAsync(cancellationToken);
        if (centroIdsVisibles is not null)
            filasBloqueantes = filasBloqueantes.Where(f => centroIdsVisibles.Contains(f.CentroId)).ToList();

        if (filasBloqueantes.Count == 0)
            return [];

        var centroIds = filasBloqueantes.Select(f => f.CentroId).Distinct().ToList();
        var tipoIds = filasBloqueantes.Select(f => f.TipoDocumentoId).Distinct().ToList();

        var centros = await centrosContext.Centros
            .Where(c => centroIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Nombre })
            .ToDictionaryAsync(c => c.Id, c => c.Nombre, cancellationToken);

        var clientesPorCentro = await (
            from centro in centrosContext.Centros
            where centroIds.Contains(centro.Id)
            join cliente in clientesContext.Clientes on centro.ClienteId equals cliente.Id
            select new { centro.Id, ClienteId = cliente.Id, cliente.RazonSocial })
            .ToDictionaryAsync(x => x.Id, x => (x.ClienteId, x.RazonSocial), cancellationToken);

        var tipos = await tiposDocumentoContext.TiposDocumento
            .Where(t => tipoIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Nombre })
            .ToDictionaryAsync(t => t.Id, t => t.Nombre, cancellationToken);

        var asignaciones = await (
            from asignacion in asignacionesContext.Asignaciones
            where asignacion.FechaBaja == null && centroIds.Contains(asignacion.CentroId)
            join trabajador in trabajadoresContext.Trabajadores on asignacion.TrabajadorId equals trabajador.Id
            select new
            {
                asignacion.CentroId,
                TrabajadorId = trabajador.Id,
                TrabajadorNombre = trabajador.Nombre + " " + trabajador.Apellidos,
                trabajador.EmpresaId
            })
            .ToListAsync(cancellationToken);

        if (asignaciones.Count == 0)
            return [];

        var empresaIds = asignaciones.Where(a => a.EmpresaId is not null).Select(a => a.EmpresaId!.Value).Distinct().ToList();
        var nombresPorEmpresa = empresaIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await empresasContext.Empresas
                .Where(e => empresaIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id, e => e.RazonSocial, cancellationToken);

        var trabajadorIds = asignaciones.Select(a => a.TrabajadorId).Distinct().ToList();

        var parejasConDocumentoVigente = (await documentosContext.Documentos
            .Where(d => d.TrabajadorId != null
                && trabajadorIds.Contains(d.TrabajadorId!.Value)
                && tipoIds.Contains(d.TipoDocumentoId)
                && (d.FechaVencimiento == null || d.FechaVencimiento >= DateOnly.FromDateTime(DateTime.UtcNow)))
            .Select(d => new { TrabajadorId = d.TrabajadorId!.Value, d.TipoDocumentoId })
            .ToListAsync(cancellationToken))
            .Select(d => (d.TrabajadorId, d.TipoDocumentoId))
            .ToHashSet();

        var tiposBloqueantesPorCentro = filasBloqueantes
            .GroupBy(f => f.CentroId)
            .ToDictionary(g => g.Key, g => g.Select(f => f.TipoDocumentoId).ToHashSet());

        var pendientes = new List<DocumentacionBloqueantePendienteDto>();

        foreach (var fila in filasBloqueantes)
        {
            if (!centros.TryGetValue(fila.CentroId, out var centroNombre)) continue;
            if (!tipos.TryGetValue(fila.TipoDocumentoId, out var tipoNombre)) continue;

            foreach (var asignacion in asignaciones.Where(a => a.CentroId == fila.CentroId))
            {
                if (parejasConDocumentoVigente.Contains((asignacion.TrabajadorId, fila.TipoDocumentoId)))
                    continue;

                var cliente = clientesPorCentro.TryGetValue(fila.CentroId, out var c) ? c : ((Guid?)null, (string?)null);
                var empresaNombre = asignacion.EmpresaId is { } empresaId && nombresPorEmpresa.TryGetValue(empresaId, out var nombreEmpresa)
                    ? nombreEmpresa
                    : null;
                var esAltaNueva = !tiposBloqueantesPorCentro[fila.CentroId]
                    .Any(tipoId => parejasConDocumentoVigente.Contains((asignacion.TrabajadorId, tipoId)));

                pendientes.Add(new DocumentacionBloqueantePendienteDto(
                    fila.CentroId, centroNombre, asignacion.TrabajadorId, asignacion.TrabajadorNombre,
                    fila.TipoDocumentoId, tipoNombre,
                    ClienteId: cliente.Item1, ClienteNombre: cliente.Item2,
                    EmpresaId: asignacion.EmpresaId, EmpresaNombre: empresaNombre,
                    EsAltaNueva: esAltaNueva));
            }
        }

        return pendientes.OrderBy(p => p.CentroNombre).ThenBy(p => p.TrabajadorNombre).ToList();
    }
}
