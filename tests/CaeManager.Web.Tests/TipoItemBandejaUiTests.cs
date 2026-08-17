using CaeManager.Application.Bandeja.Queries.ObtenerBandejaGestor;
using CaeManager.Web.Components.DesignSystem;
using CaeManager.Web.Features.Bandeja;
using FluentAssertions;

namespace CaeManager.Web.Tests;

/// <summary>
/// Un RequisitoPendiente con EsAltaNueva (Trabajador sin ningún documento
/// vigente de los tipos bloqueantes en ese Centro — nunca llegó a completar
/// el alta) debe leerse distinto de un requisito que sí bloquea acceso por
/// una regresión (algo caducó): ni el badge ni la acción primaria deben
/// alarmar como si el centro se hubiera roto.
/// </summary>
public class TipoItemBandejaUiTests
{
    private static ItemBandejaDto Requisito(bool esAltaNueva) => new(
        Id: "requisito-1", Tipo: TipoItemBandeja.RequisitoPendiente, Titulo: "PSS firmado — Ana García",
        Subtitulo: "Centro Sur", Fecha: null, TrabajadorId: Guid.NewGuid(), CentroId: Guid.NewGuid(),
        DocumentoId: null, TipoDocumentoId: Guid.NewGuid(), RequisitoId: null, EsAltaNueva: esAltaNueva);

    [Fact]
    public void Alta_nueva_usa_tono_de_advertencia_no_de_peligro()
    {
        TipoItemBandejaUi.Tono(Requisito(esAltaNueva: true)).Should().Be(TonoBadge.Advertencia);
    }

    [Fact]
    public void Visita_tradicional_sigue_usando_tono_de_peligro()
    {
        TipoItemBandejaUi.Tono(Requisito(esAltaNueva: false)).Should().Be(TonoBadge.Peligro);
    }

    [Fact]
    public void Alta_nueva_dice_alta_pendiente_en_vez_de_bloquea_el_centro()
    {
        TipoItemBandejaUi.Texto(Requisito(esAltaNueva: true)).Should().Be("Alta pendiente");
        TipoItemBandejaUi.Texto(Requisito(esAltaNueva: false)).Should().Be("Bloquea el centro");
    }

    [Fact]
    public void Alta_nueva_ofrece_adjuntar_en_vez_de_ver_requisito()
    {
        TipoItemBandejaUi.TextoAccion(Requisito(esAltaNueva: true)).Should().Be("Adjuntar");
        TipoItemBandejaUi.TextoAccion(Requisito(esAltaNueva: false)).Should().Be("Ver requisito");
    }
}
