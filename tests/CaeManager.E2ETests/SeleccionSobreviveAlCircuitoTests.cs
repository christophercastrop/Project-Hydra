using System.Collections.Generic;

using Microsoft.Playwright;

namespace CaeManager.E2ETests;

/// <summary>
/// <b>¿Sobrevive la selección de workspace a una interacción PURA de circuito de
/// Blazor — sin ninguna petición HTTP nueva?</b>
///
/// <para>
/// La selección (workspace activo y, en el plano 3, sesión privilegiada) vive en una
/// cookie que <c>ClienteActivoSeleccionado</c> lee por <c>IHttpContextAccessor</c>.
/// Dentro de un circuito de Blazor Server ese <c>HttpContext</c> puede no existir, y
/// entonces la selección resuelve a nulo <b>y memoiza ese nulo</b> para todo el ámbito
/// de DI — medido en <c>SeleccionSinHttpContextTests</c> (Web.Tests).
/// </para>
///
/// <para>
/// <b>REC-137 — por qué la versión anterior de este test no medía lo que decía
/// medir.</b> Su guarda solo comprobaba que el documento no se hubiera recargado
/// (una marca en <c>window</c> seguía viva). Medido aquí con <c>page.Request</c>: un
/// clic en un enlace del menú (<c>&lt;a class="nav-item"&gt;</c>) navega sin recargar
/// el documento, pero **sí** dispara una petición HTTP real (<i>enhanced navigation</i>
/// de Blazor Web Apps, un <c>fetch</c> al servidor que reutiliza la misma conexión de
/// SignalR). Esa petición pasa por el pipeline de ASP.NET Core igual que cualquier
/// otra — <c>RevalidacionClienteActivoMiddleware</c> incluido — así que el test
/// anterior probaba la revalidación por HTTP, no el circuito. Medido también con
/// <c>Blazor.navigateTo(uri, forceLoad:false)</c> y con un cambio de query string vía
/// <c>NavigationManager.NavigateTo(uri, replace:true)</c> desde un componente
/// interactivo: los tres disparan la misma petición HTTP. Esta aplicación resuelve el
/// enrutado con <c>&lt;Router&gt;</c> estático (<c>Routes.razor</c> sin
/// <c>@rendermode</c>) y cada página declara su propio <c>@rendermode</c> — con esa
/// configuración, **todo** cambio de URL, entre páginas o de query string en la misma
/// página, pasa por la vía de <i>enhanced navigation</i>. No hay ningún mecanismo de
/// UI en esta aplicación, hoy, que cambie de URL sin una petición HTTP — confirmado,
/// además, por el propio comentario de <c>wwwroot/js/tema.js:6-9</c>: "La navegación
/// 'enhanced' de Blazor (la que dispara cualquier clic en el menú lateral) vuelve a
/// pedir el HTML de la página al servidor".
/// </para>
///
/// <para>
/// <b>La interacción que sí es pura de circuito.</b> Un evento de un componente
/// interactivo que NO llama a <c>NavigationManager</c> se despacha íntegramente por la
/// conexión de SignalR ya abierta, sin ninguna petición HTTP — es el caso de
/// <see cref="Components.Layout.SelectorTema"/> (<c>CambiarTemaAsync</c>: solo
/// interop de JS y <c>UserManager.UpdateAsync</c>, cero <c>NavigationManager</c>,
/// confirmado leyendo su código). Cambiar el tema desde la página ya cargada es, por
/// tanto, la interacción de circuito puro más simple y fiable disponible en esta
/// aplicación — no requiere activar ningún modo adicional ni depender de datos
/// concretos de la siembra.
/// </para>
///
/// <para>
/// <b>Lo que está en juego.</b> Si la selección se pierde en el circuito, la
/// consecuencia inmediata es que <c>ITenantActual</c> resuelve al tenant de origen
/// dentro del workspace ajeno. Y para el plano 3 es peor:
/// <c>TenantRlsConnectionInterceptor</c> adopta el rol de solo lectura
/// <c>cae_app_soporte</c> <b>solo</b> cuando la sesión privilegiada no es nula, así
/// que la garantía que sostiene la decisión D-2 —el soporte no conserva escritura— no
/// aplicaría a nada de lo que ocurre por el circuito. El fallo sería silencioso: nada
/// se rompe, simplemente la protección no está.
/// </para>
/// </summary>
[Collection("AppCollection")]
public class SeleccionSobreviveAlCircuitoTests(WebAppFixture fixture)
{
    [Fact]
    public async Task La_seleccion_de_workspace_sobrevive_a_una_interaccion_pura_de_circuito()
    {
        await using var contexto = await fixture.Browser.NewContextAsync();
        var page = await contexto.NewPageAsync();

        await Ayudas.IniciarSesionAsync(
            page, fixture.BaseUrl, Ayudas.EmailAdministradorConsultora, Ayudas.ContrasenaUsuariosPrueba);
        await Ayudas.DescartarNotificacionesPendientesAsync(page);

        // ── Línea base, en el mismo fixture ────────────────────────────────────
        // El tenant de origen del Administrador es la Consultora, que no tiene datos
        // operativos propios (ADR-004 § 5.1; los ~200 Clientes de la siembra viven en
        // su Delegated Workspace). Comprobarlo aquí es lo que da valor a las
        // aserciones de más abajo: sin esta línea base, "hay empresas" podría
        // significar simplemente que el origen también las tenía.
        await Ayudas.NavegarYEsperarAsync(page, $"{fixture.BaseUrl}/empresas");
        await Assertions.Expect(page.GetByText("Aún no hay empresas"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // ── Cambio de workspace (HTTP, forma parte del contrato: la propia
        // ClienteActivoSeleccionado documenta que cambiar de cliente exige un reload
        // completo del navegador) ──────────────────────────────────────────────
        // Se cambia estando YA en /empresas, no desde "/": SelectorClienteActivo
        // envía returnUrl con la página actual (SelectorClienteActivo.razor.cs:64),
        // así que el reload aterriza directamente de vuelta en /empresas —un solo
        // circuito nuevo, no dos. La versión anterior de este test volvía a "/"
        // primero porque necesitaba una navegación de página real que medir; esta
        // versión mide la interacción de circuito puro de más abajo, así que ese
        // segundo salto ya no hace falta y solo añadía una apertura de circuito
        // extra sin motivo.
        await Ayudas.CambiarClienteActivoAsync(page, fixture.BaseUrl, Ayudas.NombreClienteDelegadoDemo);

        await Assertions.Expect(page.Locator(".selector-cliente-activo option:checked"))
            .ToHaveTextAsync(Ayudas.NombreClienteDelegadoDemo,
                new LocatorAssertionsToHaveTextOptions { Timeout = 15_000 });
        // Texto y timeout idénticos a la aserción de más abajo (línea ~141):
        // envuelta en try/catch con mensaje propio para que un fallo diga CUÁL
        // de las dos disparó, incluso si el stack trace colapsa (REC-137).
        try
        {
            await Assertions.Expect(page.GetByText("Aún no hay empresas")).Not.ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        }
        catch (PlaywrightException ex)
        {
            Assert.Fail(
                "[Tras el reload de CambiarClienteActivoAsync, todavía en ámbito HTTP, antes de la interacción "
                + "de circuito puro] " + ex.Message);
        }

        // ── Instrumento: peticiones HTTP observadas desde aquí ──────────────────
        var peticionesDurantePausaCircuito = new List<string>();
        void RegistrarPeticion(object? _, IRequest req) =>
            peticionesDurantePausaCircuito.Add($"{req.Method} {req.Url} ({req.ResourceType})");
        page.Request += RegistrarPeticion;

        // ── La interacción de circuito puro: cambiar el tema ────────────────────
        // CambiarTemaAsync no llama a NavigationManager (ver el <summary> de esta
        // clase) — el evento @onchange, el interop de JS y el guardado se despachan
        // íntegramente por la conexión de SignalR ya abierta.
        var selectorTema = page.Locator("select.selector-tema");
        await Assertions.Expect(selectorTema).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        await selectorTema.SelectOptionAsync("oscuro");

        // Confirma que el evento sí llegó y se procesó en el circuito (con éxito o
        // sin él, la propia respuesta visual demuestra que no hubo ninguna
        // recarga/navegación de por medio) antes de leer las peticiones acumuladas.
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync(
            "data-theme", "oscuro", new LocatorAssertionsToHaveAttributeOptions { Timeout = 15_000 });

        page.Request -= RegistrarPeticion;

        Assert.True(
            peticionesDurantePausaCircuito.Count == 0,
            "Cambiar el tema no debería generar ninguna petición HTTP (es un evento sin NavigationManager, "
            + "despachado por SignalR), pero se registraron: "
            + string.Join("; ", peticionesDurantePausaCircuito)
            + ". Si esto falla, la ventana que se medía ya no es de circuito puro y el resto de esta prueba no "
            + "mide lo que dice medir.");

        // ── La pregunta: la selección de workspace sigue viva tras la interacción
        // de circuito puro de arriba, sin que ninguna petición HTTP haya vuelto a
        // pasar por RevalidacionClienteActivoMiddleware ────────────────────────
        // Texto y timeout idénticos a la aserción de más arriba (línea ~104):
        // mismo motivo, mensaje propio (REC-137).
        try
        {
            await Assertions.Expect(page.GetByText("Aún no hay empresas")).Not.ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        }
        catch (PlaywrightException ex)
        {
            Assert.Fail(
                "[Tras la interacción de circuito puro, con la guarda de peticiones HTTP satisfecha] "
                + ex.Message);
        }
    }

    /// <summary>
    /// Control positivo de REC-137, exigido por HO-136-01 § 8/§ 17: si el instrumento
    /// de arriba (contar peticiones HTTP) no distinguiera nada, un cero peticiones no
    /// significaría "circuito puro" — significaría "Playwright no ve las peticiones".
    /// Este test demuestra que SÍ las ve: la misma navegación por clic que el test
    /// original usaba (creyéndola "dentro del circuito") deja al menos una petición
    /// HTTP real al destino, así que aplicar la misma guarda de cero peticiones a esa
    /// navegación fallaría — por el motivo correcto.
    /// </summary>
    [Fact]
    public async Task Control_positivo_la_navegacion_por_clic_en_el_menu_SI_genera_una_peticion_http()
    {
        await using var contexto = await fixture.Browser.NewContextAsync();
        var page = await contexto.NewPageAsync();

        await Ayudas.IniciarSesionAsync(
            page, fixture.BaseUrl, Ayudas.EmailAdministradorConsultora, Ayudas.ContrasenaUsuariosPrueba);
        await Ayudas.DescartarNotificacionesPendientesAsync(page);
        await Ayudas.NavegarYEsperarAsync(page, fixture.BaseUrl);

        var peticiones = new List<string>();
        page.Request += (_, req) => peticiones.Add($"{req.Method} {req.Url}");

        var enlaceEmpresas = page.Locator("a.nav-item[href='empresas']").First;
        await Assertions.Expect(enlaceEmpresas).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        await enlaceEmpresas.ClickAsync();

        await Assertions.Expect(page).ToHaveURLAsync(
            new System.Text.RegularExpressions.Regex("/empresas$"),
            new PageAssertionsToHaveURLOptions { Timeout = 15_000 });

        Assert.True(
            peticiones.TrueForAll(p => !p.Contains("/empresas")) == false,
            "Este test debía demostrar que el clic en el menú SÍ genera una petición HTTP al destino — si esto "
            + "falla, el propio control positivo dejó de ser válido y el cero peticiones del test principal no "
            + "prueba nada. Peticiones observadas: " + string.Join("; ", peticiones));
    }
}
