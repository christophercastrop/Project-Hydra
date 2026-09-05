using Microsoft.Playwright;

namespace CaeManager.E2ETests;

/// <summary>
/// <b>¿Sobrevive la selección de workspace a una navegación DENTRO del circuito de
/// Blazor?</b>
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
/// <b>Por qué ningún test previo lo cubría.</b> Los cuatro E2E que cambian de
/// workspace navegan con <c>page.GotoAsync</c> o con el <c>&lt;form&gt;</c> del
/// selector, es decir con una petición HTTP completa donde <c>HttpContext</c> sí
/// existe. Su verde no dice nada sobre el circuito. Este test es el primero que
/// navega <b>sin recargar el documento</b>.
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
    /// <summary>
    /// Marca puesta en <c>window</c> antes de navegar. Si sigue ahí después, el
    /// documento <b>no</b> se recargó y la navegación fue del router de Blazor. Sin
    /// esta comprobación el test podría pasar por el motivo contrario al que
    /// investiga: una recarga completa trae <c>HttpContext</c> y la selección
    /// funcionaría igual, sin haber ejercitado el circuito.
    /// </summary>
    private const string MarcaDeCircuito = "__marcaSinRecargaDeDocumento";

    [Fact]
    public async Task La_seleccion_de_workspace_sigue_viva_tras_navegar_dentro_del_circuito()
    {
        await using var contexto = await fixture.Browser.NewContextAsync();
        var page = await contexto.NewPageAsync();

        await Ayudas.IniciarSesionAsync(
            page, fixture.BaseUrl, Ayudas.EmailOperadorConsultaConsultora, Ayudas.ContrasenaUsuariosPrueba);
        await Ayudas.DescartarNotificacionesPendientesAsync(page);

        // ── Línea base, en el mismo fixture ────────────────────────────────────
        // El tenant de origen de este Operador Delegado es la Consultora, que no
        // tiene datos operativos propios (ADR-004 § 5.1). Comprobarlo aquí es lo
        // que da valor a la aserción final: sin esta línea base, "hay empresas" al
        // final podría significar simplemente que el origen también las tenía.
        await Ayudas.NavegarYEsperarAsync(page, $"{fixture.BaseUrl}/empresas");
        await Assertions.Expect(page.GetByText("Aún no hay empresas"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // ── Cambio de workspace ───────────────────────────────────────────────
        // HO-136-05: el primer intento de arreglar este test usaba el
        // Administrador de ArcoSPA cambiando a NombreClienteDelegadoDemo (Dexter)
        // — y con los dos instrumentos ya corregidos, la aserción final se puso
        // en rojo de forma estable (33 reintentos sobre 15 s, no una carrera
        // puntual). Un diagnóstico aparte confirmó que el mismo vacío aparece
        // incluso con un FULL RELOAD a /empresas (page.GotoAsync, sin circuito de
        // por medio): no es un fallo del circuito, es que ese Administrador
        // opera Dexter con rol GestorCae y cero AsignacionCartera — alcance cero
        // por diseño, ya documentado y verificado por mutación en
        // AlcanceRolesTests.cs. Ese test tenía la pregunta correcta con el actor
        // equivocado.
        //
        // Este Operador Delegado tiene rol Consulta sobre el mismo
        // NombreClienteDelegadoDemo (ver DelegacionDemoSeeder.
        // SembrarOperadoresConsultoraAsync), y Consulta tiene acceso total sin
        // depender de cartera (AlcanceDatosService.TieneAccesoTotalAsync) — así
        // que si la selección de workspace se pierde en el circuito, la única
        // explicación de un "Aún no hay empresas" en la aserción final vuelve a
        // ser el propio circuito, no un alcance vacío por delegación.
        //
        // Vuelve al inicio primero: el selector redirige a returnUrl, y si el cambio
        // ocurriera estando ya en /empresas el paso siguiente no navegaría a ningún
        // sitio y no habría navegación de circuito que medir.
        await Ayudas.NavegarYEsperarAsync(page, fixture.BaseUrl);
        await Ayudas.CambiarClienteActivoAsync(page, fixture.BaseUrl, Ayudas.NombreClienteDelegadoDemo);

        // ── La navegación de circuito ─────────────────────────────────────────
        // Esperar a que el cambio de workspace haya ASENTADO antes de tocar el
        // contexto de JS. El <form> del selector dispara una navegación completa, y
        // EvaluateAsync no reintenta: sin esta espera revienta con "Execution context
        // was destroyed" (visto en la primera ejecución de este test). Un Locator sí
        // se vuelve a resolver contra el DOM actual, así que esta aserción sirve de
        // barrera.
        //
        // HO-136-05: esta comprobación por sí sola NO demuestra que el cambio surtió
        // efecto — SelectOptionAsync marca el <option> como seleccionado en el
        // cliente antes de que el formulario llegue a enviarse, así que leerlo aquí
        // podía estar viendo la marca de Playwright, no el resultado del servidor.
        // Ayudas.CambiarClienteActivoAsync ya exige un 3xx real del POST a
        // /cuenta/cliente-activo antes de devolver el control (esa es la prueba de
        // servidor); esta aserción queda como comprobación adicional, sobre el DOM ya
        // asentado tras esa redirección, de que el <select> también quedó consistente
        // con lo que el servidor aplicó.
        await Assertions.Expect(page.Locator(".selector-cliente-activo option:checked"))
            .ToHaveTextAsync(Ayudas.NombreClienteDelegadoDemo,
                new LocatorAssertionsToHaveTextOptions { Timeout = 15_000 });

        await page.EvaluateAsync($"window.{MarcaDeCircuito} = true;");

        var enlaceEmpresas = page.Locator("a.nav-item[href='empresas']").First;
        await Assertions.Expect(enlaceEmpresas).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        await enlaceEmpresas.ClickAsync();

        await Assertions.Expect(page).ToHaveURLAsync(
            new System.Text.RegularExpressions.Regex("/empresas$"),
            new PageAssertionsToHaveURLOptions { Timeout = 15_000 });

        // Guarda del instrumento, antes de la aserción que importa: si el documento
        // se recargó, este test no ha medido el circuito y su resultado —verde o
        // rojo— no responde a la pregunta.
        var marcaSobrevive = await page.EvaluateAsync<bool?>($"window.{MarcaDeCircuito} ?? null");
        Assert.True(
            marcaSobrevive is true,
            "el clic en el menú recargó el documento entero, así que esta ejecución NO ha ejercitado la " +
            "navegación dentro del circuito. El test no ha medido lo que dice medir: hay que encontrar otra " +
            "vía de navegación que no recargue antes de creerse su resultado.");

        // ── La pregunta ───────────────────────────────────────────────────────
        // HO-136-05: "ToHaveURL" no es una barrera de contenido. Con enhanced
        // navigation la URL cambia antes de que el parche del DOM aterrice —
        // medido en 5/5 iteraciones, el estado vacío tardaba entre 33 y 144 ms
        // más en aparecer tras el cambio de URL. `Not.ToBeVisibleAsync` se
        // satisface con la PRIMERA comprobación en la que el texto no está
        // visible, y eso es trivialmente cierto en la ventana en la que
        // Empresas.razor todavía no ha terminado de cargar (muestra
        // EstadoCargando, ningún texto todavía) — o sea, el assert de abajo
        // podía dar verde sin que el contenido real hubiera llegado a
        // decidirse. Antes de leerlo, se espera a que la página alcance
        // alguno de sus tres estados terminales y mutuamente excluyentes
        // (lista con filas, "Aún no hay empresas" o el estado de error): solo
        // entonces "no está visible" significa "la carga terminó y no es
        // este", no "todavía no ha cargado nada".
        var estadoAsentado = page.Locator(".lista-filas-acordeon")
            .Or(page.GetByText("Aún no hay empresas"))
            .Or(page.GetByText("No pudimos cargar las empresas"));
        await Assertions.Expect(estadoAsentado).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        await Assertions.Expect(page.GetByText("Aún no hay empresas")).Not.ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
    }
}
