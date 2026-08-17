using Bunit;
using CaeManager.Application.Bandeja.Queries.ObtenerBandejaAgrupada;
using CaeManager.Application.Bandeja.Queries.ObtenerBandejaGestor;
using CaeManager.Web.Components.DesignSystem;
using CaeManager.Web.Components.Workspace;
using CaeManager.Web.Features.Bandeja.Components;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace CaeManager.Web.Tests;

/// <summary>
/// GrupoColaDto agrupa por Cliente/Empresa, nunca por Centro (ver
/// ObtenerBandejaAgrupadaQueryHandler.ClaveGrupo) — un grupo real con
/// muchos trabajadores puede pertenecer a Centros distintos. El truncado
/// (decisión de producto 2026-08-17) corta por TRABAJADOR y solo enlaza a
/// un Centro concreto cuando todos los ocultos comparten el mismo; si no,
/// cae a un enlace genérico a Mi trabajo.
/// </summary>
public class GrupoColaTests : BunitContext
{
    public GrupoColaTests()
    {
        Services.AddSingleton<ContextWorkspaceService>();
        Services.AddSingleton<ToastService>();
        // TextoFechaCopiable (dentro de PanelResolverItem) importa clipboard.js
        // en OnAfterRenderAsync — no hay nada que copiar en este test, así que
        // basta con dejar pasar la importación en vez de mockear el módulo entero.
        JSInterop.SetupModule("./js/clipboard.js");
    }

    private static ItemBandejaDto Item(int n, Guid centroId) => new(
        Id: $"item-{n}", Tipo: TipoItemBandeja.RequisitoPendiente, Titulo: $"PSS firmado — Trabajador {n}",
        Subtitulo: "Centro X", Fecha: null, TrabajadorId: Guid.NewGuid(), CentroId: centroId,
        DocumentoId: null, TipoDocumentoId: Guid.NewGuid(), RequisitoId: null,
        TrabajadorNombre: $"Trabajador {n}");

    [Fact]
    public void Trunca_a_partir_del_sexto_trabajador_con_enlace_al_centro_unico()
    {
        var centroId = Guid.NewGuid();
        var items = Enumerable.Range(1, 7).Select(n => Item(n, centroId)).ToList();
        var grupo = new GrupoColaDto("cliente-1", "Cliente X", true, items);

        var cut = Render<GrupoCola>(p => p.Add(c => c.Grupo, grupo).Add(c => c.ExpandidaPorDefecto, true));

        cut.FindAll(".grupo-cola-subcabecera-trabajador").Should().HaveCount(5);
        var enlace = cut.Find(".grupo-cola-truncado a");
        enlace.TextContent.Should().Contain("Ver 2 trabajadores más en este Centro");
        enlace.GetAttribute("href").Should().Be($"/centros/{centroId}");
    }

    [Fact]
    public void Sin_centro_comun_entre_los_ocultos_enlaza_a_mi_trabajo()
    {
        // Cada Item recibe su propio Guid.NewGuid() — los dos ocultos (6º y 7º)
        // acaban con Centros distintos entre sí, sin necesidad de forzarlo.
        var items = Enumerable.Range(1, 7).Select(n => Item(n, Guid.NewGuid())).ToList();
        var grupo = new GrupoColaDto("cliente-1", "Cliente X", true, items);

        var cut = Render<GrupoCola>(p => p.Add(c => c.Grupo, grupo).Add(c => c.ExpandidaPorDefecto, true));

        var enlace = cut.Find(".grupo-cola-truncado a");
        enlace.TextContent.Should().Contain("Ver 2 trabajadores más en Mi trabajo");
        enlace.GetAttribute("href").Should().Be("/bandeja");
    }

    [Fact]
    public void No_trunca_con_cinco_trabajadores_o_menos()
    {
        var items = Enumerable.Range(1, 5).Select(n => Item(n, Guid.NewGuid())).ToList();
        var grupo = new GrupoColaDto("cliente-1", "Cliente X", true, items);

        var cut = Render<GrupoCola>(p => p.Add(c => c.Grupo, grupo).Add(c => c.ExpandidaPorDefecto, true));

        cut.FindAll(".grupo-cola-subcabecera-trabajador").Should().HaveCount(5);
        cut.FindAll(".grupo-cola-truncado").Should().BeEmpty();
    }
}
