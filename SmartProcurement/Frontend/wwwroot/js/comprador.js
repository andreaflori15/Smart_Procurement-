const sel = document.getElementById("selComprador");
const btnPendientes = document.getElementById("btnPendientes");
const btnSolicitudes = document.getElementById("btnSolicitudes");
const btnClasificar = document.getElementById("btnClasificar");
const btnVolver = document.getElementById("btnVolver");
const btnVolverSolicitudes = document.getElementById("btnVolverSolicitudes");
const pasoPendientes = document.getElementById("pasoPendientes");
const pasoClasificado = document.getElementById("pasoClasificado");
const pasoSolicitudes = document.getElementById("pasoSolicitudes");
const listaSolicitudesEl = document.getElementById("listaSolicitudes");
const detalleSolicitudEl = document.getElementById("detalleSolicitud");
const hintSolicitudes = document.getElementById("hintSolicitudes");
const estadoSolicitudes = document.getElementById("estadoSolicitudes");
const cubetasEl = document.getElementById("cubetas");
const bannerEl = document.getElementById("bannerTratamiento");
const vistaEl = document.getElementById("vistaClasificada");
const filtroEstadoEl = document.getElementById("filtroEstado");
const hintPendientes = document.getElementById("hintPendientes");
const estadoFiltro = document.getElementById("estadoFiltro");
const estadoPendientes = document.getElementById("estadoPendientes");
const estadoClasificado = document.getElementById("estadoClasificado");

const COLUMNAS_PENDIENTES = [
    "SOLPED", "NPOs", "Centro", "Material", "Texto_Breve",
    "Cantidad_Solicitada", "RazSocial", "Email", "RANGO_FECHA", "TipoCompra_Den"
];
const COLUMNAS_DETALLE = ["solped", "posicion", "proveedoresCotizados", "estado", "fechaRespuestaProveedor"];
const COLUMNAS_ITEM = [
    "SOLPED", "NPOs", "Centro", "Material", "Texto_Breve",
    "Cantidad_Solicitada", "RazSocial", "ImporteUsd", "Contrato"
];
const FILTROS_ESTADO = [
    ["todas", "Todas"],
    ["sin_solicitar", "Sin solicitar"],
    ["ya_solicitadas", "Ya solicitadas"]
];
const PILL = {
    sin_solicitar: "",
    pendiente_proveedor: "es-pendiente",
    ofertas_parciales: "es-parcial",
    listo_comparativo: "es-ok",
    confirmar_usuario: "es-pendiente",
    pedido_sap: "es-ok"
};

let clasificacion = { cubetas: [], datos: [], lotes: {} };
let cubetaActiva = "contrato";
let filtroEstado = "todas";
let solicitudActiva = null;
let solicitudesCache = [];

async function cargarCompradores() {
    try {
        const r = await fetch("/Comprador/Compradores");
        const data = await r.json();
        sel.replaceChildren();
        const vacio = document.createElement("option");
        vacio.value = "";
        vacio.textContent = "Elige un comprador";
        sel.appendChild(vacio);

        (data.compradores || []).forEach((nombre) => {
            const opt = document.createElement("option");
            opt.value = nombre;
            opt.textContent = nombre;
            sel.appendChild(opt);
        });

        if ((data.compradores || []).length === 1) {
            sel.value = data.compradores[0];
        }

        estadoFiltro.textContent = data.demo ? "datos de prueba" : "";
    } catch {
        window.SP.toast("No pude cargar la lista de compradores.", true);
        sel.replaceChildren();
        const opt = document.createElement("option");
        opt.textContent = "No hay compradores";
        sel.appendChild(opt);
    }
}

btnPendientes.addEventListener("click", async () => {
    const comprador = sel.value.trim();
    if (!comprador) {
        window.SP.toast("Elige un comprador.", true);
        return;
    }

    window.SP.setLoading(btnPendientes, true, "Ver pendientes");
    try {
        const r = await fetch("/Comprador/Pendientes", {
            method: "POST",
            headers: { "Content-Type": "application/x-www-form-urlencoded" },
            body: "comprador=" + encodeURIComponent(comprador)
        });
        const data = await r.json();
        if (!data.valido) {
            window.SP.toast(data.mensaje || "No se pudo cargar.", true);
            return;
        }

        pasoClasificado.classList.add("is-hidden");
        pasoSolicitudes.classList.add("is-hidden");
        pasoPendientes.classList.remove("is-hidden");
        hintPendientes.textContent = "Pendientes de " + comprador + ", todavía sin pedido.";
        const wrap = document.getElementById("wrapPendientes");
        wrap.replaceChildren(crearTabla(data.datos, COLUMNAS_PENDIENTES));
        estadoPendientes.textContent = (data.datos?.length || 0) + " SOLPEDs" + (data.demo ? " · datos de prueba" : "");
    } catch {
        window.SP.toast("No pude hablar con el servidor.", true);
    } finally {
        window.SP.setLoading(btnPendientes, false, "Ver pendientes");
    }
});

btnClasificar.addEventListener("click", async () => {
    const comprador = sel.value.trim();
    if (!comprador) {
        window.SP.toast("Elige un comprador.", true);
        return;
    }

    window.SP.setLoading(btnClasificar, true, "Clasificar");
    try {
        const r = await fetch("/Comprador/Clasificar", {
            method: "POST",
            headers: { "Content-Type": "application/x-www-form-urlencoded" },
            body: "comprador=" + encodeURIComponent(comprador)
        });
        const data = await r.json();
        if (!data.valido) {
            window.SP.toast(data.mensaje || "No se pudo clasificar.", true);
            return;
        }

        clasificacion = data;
        clasificacion.lotes = data.lotes || {};
        cubetaActiva = (data.cubetas || []).find((c) => c.count > 0)?.id || "contrato";
        filtroEstado = "todas";
        pasoPendientes.classList.add("is-hidden");
        pasoSolicitudes.classList.add("is-hidden");
        pasoClasificado.classList.remove("is-hidden");
        estadoClasificado.textContent = (data.datos?.length || 0) + " clasificadas" + (data.demo ? " · datos de prueba" : "");
        pintarCubetas();
        pintarFiltroEstado();
        pintarVista();
    } catch {
        window.SP.toast("No pude hablar con el servidor.", true);
    } finally {
        window.SP.setLoading(btnClasificar, false, "Clasificar");
    }
});

btnVolver.addEventListener("click", () => {
    pasoClasificado.classList.add("is-hidden");
    pasoSolicitudes.classList.add("is-hidden");
    pasoPendientes.classList.remove("is-hidden");
});

btnSolicitudes.addEventListener("click", () => cargarSolicitudes());
btnVolverSolicitudes.addEventListener("click", () => {
    pasoSolicitudes.classList.add("is-hidden");
    detalleSolicitudEl.classList.add("is-hidden");
    solicitudActiva = null;
});

function pintarCubetas() {
    cubetasEl.replaceChildren();
    (clasificacion.cubetas || []).forEach((c) => {
        const btn = document.createElement("button");
        btn.type = "button";
        btn.className = "cubeta" + (c.id === cubetaActiva ? " is-on" : "");
        btn.innerHTML = "<strong>" + c.titulo + "</strong><span>" + c.count + "</span>";
        btn.addEventListener("click", () => {
            cubetaActiva = c.id;
            pintarCubetas();
            pintarVista();
        });
        cubetasEl.appendChild(btn);
    });
}

function pintarFiltroEstado() {
    filtroEstadoEl.replaceChildren();
    FILTROS_ESTADO.forEach(([id, label]) => {
        const b = document.createElement("button");
        b.type = "button";
        b.className = "chip" + (filtroEstado === id ? " is-on" : "");
        b.textContent = label;
        b.addEventListener("click", () => {
            filtroEstado = id;
            pintarFiltroEstado();
            pintarVista();
        });
        filtroEstadoEl.appendChild(b);
    });
}

function estadoDe(loteId) {
    const lote = (clasificacion.lotes || {})[loteId];
    return lote || {
        loteId,
        estadoId: "sin_solicitar",
        estado: "Sin solicitar",
        ofertas: 0,
        ofertasNecesarias: cubetaActiva === "licitacion" ? 2 : cubetaActiva === "contrato" ? 0 : 1
    };
}

function pasaFiltroLote(est) {
    if (filtroEstado === "todas") return true;
    if (filtroEstado === "sin_solicitar") return !est.estadoId || est.estadoId === "sin_solicitar";
    return est.estadoId && est.estadoId !== "sin_solicitar";
}

function pintarVista() {
    const cubeta = (clasificacion.cubetas || []).find((c) => c.id === cubetaActiva);
    const filas = (clasificacion.datos || []).filter((f) => f.Cubeta === cubetaActiva);
    bannerEl.textContent = cubeta ? cubeta.tratamiento : "";

    vistaEl.replaceChildren();
    if (filas.length === 0) {
        const vacio = document.createElement("p");
        vacio.className = "empty";
        vacio.textContent = "No hay SOLPEDs en esta clasificación.";
        vistaEl.appendChild(vacio);
        return;
    }

    const grupos = agrupar(filas).filter((g) => pasaFiltroLote(estadoDe(g.loteId)));
    if (grupos.length === 0) {
        const vacio = document.createElement("p");
        vacio.className = "empty";
        vacio.textContent = filtroEstado === "ya_solicitadas"
            ? "Todavía no has solicitado cotización en esta clasificación."
            : "No hay lotes sin solicitar.";
        vistaEl.appendChild(vacio);
        return;
    }

    grupos.forEach((grupo) => vistaEl.appendChild(tarjetaGrupo(grupo)));
}

function agrupar(filas) {
    const mapa = new Map();
    filas.forEach((f) => {
        const key = f.LoteId || f.Grupo || "sin";
        if (!mapa.has(key)) mapa.set(key, []);
        mapa.get(key).push(f);
    });
    return [...mapa.entries()]
        .sort((a, b) => b[1].length - a[1].length)
        .map(([loteId, items]) => ({
            loteId,
            nombre: items[0].LoteNombre || items[0].Grupo || loteId,
            motivo: items[0].MotivoSugerencia || "",
            sugeridos: listaProveedores(items[0]),
            filas: items
        }));
}

function listaProveedores(item) {
    const raw = item.ProveedoresSugeridos;
    if (Array.isArray(raw)) return raw.filter(Boolean);
    if (typeof raw === "string" && raw.trim()) return raw.split("|").map((s) => s.trim()).filter(Boolean);
    return item.RazSocial ? [item.RazSocial] : [];
}

function tarjetaGrupo(grupo) {
    const est = estadoDe(grupo.loteId);
    const card = document.createElement("div");
    card.className = "grupo";

    const head = document.createElement("div");
    head.className = "grupo-head";
    const h = document.createElement("h3");
    h.textContent = grupo.nombre + " · " + grupo.filas.length + " SOLPED(s)";
    const pill = document.createElement("span");
    pill.className = "status-pill " + (PILL[est.estadoId] || "");
    pill.textContent = est.estado || "Sin solicitar";
    head.appendChild(h);
    head.appendChild(pill);
    card.appendChild(head);

    const p = document.createElement("p");
    p.className = "hint";
    p.textContent = grupo.motivo || "";
    card.appendChild(p);

    if (cubetaActiva === "licitacion" || grupo.sugeridos.length) {
        const label = document.createElement("p");
        label.className = "hint";
        label.style.marginBottom = "0";
        label.textContent = cubetaActiva === "licitacion"
            ? "Cotizar a (sugeridos por historial / marca / grupo artículo):"
            : "Proveedor:";
        card.appendChild(label);
        const chips = document.createElement("div");
        chips.className = "prov-chips";
        (grupo.sugeridos.length ? grupo.sugeridos : ["Sin proveedor"]).forEach((n) => {
            const c = document.createElement("span");
            c.className = "prov-chip";
            c.textContent = n;
            chips.appendChild(c);
        });
        card.appendChild(chips);
    }

    if (cubetaActiva === "licitacion" && grupo.sugeridos.length < 2) {
        const aviso = document.createElement("p");
        aviso.className = "hint";
        aviso.textContent = "Hace falta un segundo proveedor para licitar.";
        card.appendChild(aviso);
    }

    if (est.estadoId === "listo_comparativo") {
        const aviso = document.createElement("p");
        aviso.className = "hint";
        aviso.textContent = "Hay 2 ofertas. El comparativo Excel y el agente vienen en el siguiente paso.";
        card.appendChild(aviso);
    }

    card.appendChild(listaPdfs(est));
    card.appendChild(accionesGrupo(grupo, est));
    card.appendChild(crearTabla(grupo.filas, COLUMNAS_ITEM));
    return card;
}

function accionesGrupo(grupo, est) {
    const wrap = document.createElement("div");
    wrap.className = "grupo-acciones";
    const id = grupo.loteId;

    if (cubetaActiva === "contrato") {
        if (est.estadoId !== "pedido_sap") {
            wrap.appendChild(botonAccion("Correr pedido SAP", () => postLote("/Comprador/CorrerSap", { loteId: id })));
        }
        return wrap;
    }

    if (est.estadoId === "sin_solicitar") {
        wrap.appendChild(botonAccion("Solicitar cotización", () => solicitarCotizacion(grupo)));
        return wrap;
    }

    if (est.estadoId === "pendiente_proveedor" || est.estadoId === "ofertas_parciales") {
        const falta = Math.max(0, (est.ofertasNecesarias || 1) - (est.ofertas || 0));
        wrap.appendChild(zonaSubida(grupo, est, falta));
        return wrap;
    }

    if (est.estadoId === "confirmar_usuario") {
        wrap.appendChild(botonAccion("Usuario confirmó", () =>
            postLote("/Comprador/ConfirmarUsuario", { loteId: id })));
        return wrap;
    }

    return wrap;
}

function listaPdfs(est) {
    const wrap = document.createElement("div");
    const archivos = est.archivos || [];
    if (!archivos.length) {
        return wrap;
    }
    const tit = document.createElement("p");
    tit.className = "hint";
    tit.style.marginBottom = "4px";
    tit.textContent = "Cotizaciones subidas";
    wrap.appendChild(tit);
    const ul = document.createElement("ul");
    ul.className = "pdf-list";
    archivos.forEach((a) => {
        const li = document.createElement("li");
        const link = document.createElement("a");
        link.href = a.url;
        link.target = "_blank";
        link.rel = "noopener";
        link.textContent = a.nombre || "cotizacion.pdf";
        li.appendChild(link);
        const meta = document.createElement("span");
        meta.textContent = " · " + [a.proveedor, a.fecha].filter(Boolean).join(" · ");
        li.appendChild(meta);
        ul.appendChild(li);
    });
    wrap.appendChild(ul);
    return wrap;
}

function zonaSubida(grupo, est, falta) {
    const box = document.createElement("div");
    box.className = "pdf-upload";

    const need = est.ofertasNecesarias || 1;
    const hint = document.createElement("p");
    hint.className = "hint";
    hint.textContent = need > 1
        ? "Sube el PDF de cada proveedor. Faltan " + falta + " de " + need + "."
        : "Sube el PDF de la cotización del proveedor.";
    box.appendChild(hint);

    if (grupo.sugeridos.length > 1) {
        const sel = document.createElement("select");
        sel.className = "sel-proveedor";
        sel.setAttribute("aria-label", "Proveedor de esta cotización");
        const vacio = document.createElement("option");
        vacio.value = "";
        vacio.textContent = "¿De qué proveedor es este PDF?";
        sel.appendChild(vacio);
        grupo.sugeridos.forEach((n) => {
            const opt = document.createElement("option");
            opt.value = n;
            opt.textContent = n;
            sel.appendChild(opt);
        });
        box.appendChild(sel);
        box._selProveedor = sel;
    }

    const input = document.createElement("input");
    input.type = "file";
    input.accept = "application/pdf,.pdf";
    input.className = "is-hidden";
    const btn = botonAccion("Subir PDF de cotización", () => input.click());
    input.addEventListener("change", () => {
        const file = input.files && input.files[0];
        input.value = "";
        if (file) {
            const proveedor = box._selProveedor ? box._selProveedor.value : (grupo.sugeridos[0] || "");
            subirPdf(grupo.loteId, file, proveedor);
        }
    });
    box.appendChild(input);
    box.appendChild(btn);
    return box;
}

async function subirPdf(loteId, file, proveedor) {
    if (!file.name.toLowerCase().endsWith(".pdf")) {
        window.SP.toast("Solo se acepta PDF.", true);
        return;
    }
    const fd = new FormData();
    fd.append("loteId", loteId);
    fd.append("archivo", file);
    if (proveedor) fd.append("proveedor", proveedor);
    try {
        const r = await fetch("/Comprador/SubirCotizacion", { method: "POST", body: fd });
        const data = await r.json();
        if (!data.valido) {
            window.SP.toast(data.mensaje || "No se pudo subir el PDF.", true);
            return;
        }
        clasificacion.lotes[data.lote.loteId] = data.lote;
        window.SP.toast(data.lote.estado);
        pintarVista();
    } catch {
        window.SP.toast("No pude hablar con el servidor.", true);
    }
}

function botonAccion(texto, onClick) {
    const b = document.createElement("button");
    b.type = "button";
    b.className = "btn";
    b.textContent = texto;
    b.addEventListener("click", onClick);
    return b;
}

async function solicitarCotizacion(grupo) {
    const comprador = sel.value.trim();
    if (!comprador) {
        window.SP.toast("Elige un comprador.", true);
        return;
    }

    const items = JSON.stringify(grupo.filas.map((f) => ({
        SOLPED: f.SOLPED,
        NPOs: f.NPOs,
        Centro: f.Centro,
        Material: f.Material,
        Texto_Breve: f.Texto_Breve,
        Cantidad_Solicitada: f.Cantidad_Solicitada
    })));

    const body = [
        "loteId=" + encodeURIComponent(grupo.loteId),
        "cubeta=" + encodeURIComponent(cubetaActiva),
        "comprador=" + encodeURIComponent(comprador),
        "proveedores=" + encodeURIComponent(grupo.sugeridos.join("|")),
        "items=" + encodeURIComponent(items)
    ].join("&");

    try {
        const r = await fetch("/Comprador/Solicitar", {
            method: "POST",
            headers: { "Content-Type": "application/x-www-form-urlencoded" },
            body
        });
        const data = await r.json();
        if (!data.valido) {
            window.SP.toast(data.mensaje || "No se pudo solicitar.", true);
            return;
        }
        clasificacion.lotes[data.lote.loteId] = data.lote;
        const msg = data.numeroCotizacion
            ? data.lote.estado + " · " + data.numeroCotizacion
            : data.lote.estado;
        window.SP.toast(msg);
        pintarVista();
    } catch {
        window.SP.toast("No pude hablar con el servidor.", true);
    }
}

async function cargarSolicitudes() {
    const comprador = sel.value.trim();
    if (!comprador) {
        window.SP.toast("Elige un comprador.", true);
        return;
    }

    window.SP.setLoading(btnSolicitudes, true, "Ver Solicitudes de cotización");
    try {
        const r = await fetch("/Comprador/SolicitudesCotizacion", {
            method: "POST",
            headers: { "Content-Type": "application/x-www-form-urlencoded" },
            body: "comprador=" + encodeURIComponent(comprador)
        });
        const data = await r.json();
        if (!data.valido) {
            window.SP.toast(data.mensaje || "No se pudo cargar.", true);
            return;
        }

        pasoPendientes.classList.add("is-hidden");
        pasoClasificado.classList.add("is-hidden");
        pasoSolicitudes.classList.remove("is-hidden");
        detalleSolicitudEl.classList.add("is-hidden");
        solicitudActiva = null;
        hintSolicitudes.textContent = "Solicitudes de cotización de " + comprador + ".";
        estadoSolicitudes.textContent = (data.solicitudes?.length || 0) + " solicitudes";
        solicitudesCache = data.solicitudes || [];
        pintarListaSolicitudes(solicitudesCache);
    } catch {
        window.SP.toast("No pude hablar con el servidor.", true);
    } finally {
        window.SP.setLoading(btnSolicitudes, false, "Ver Solicitudes de cotización");
    }
}

function pintarListaSolicitudes(solicitudes) {
    listaSolicitudesEl.replaceChildren();
    if (!solicitudes.length) {
        const vacio = document.createElement("p");
        vacio.className = "empty";
        vacio.textContent = "Todavía no hay solicitudes de cotización para este comprador.";
        listaSolicitudesEl.appendChild(vacio);
        return;
    }

    const wrap = document.createElement("div");
    wrap.className = "solicitud-lista";
    solicitudes.forEach((s) => {
        const btn = document.createElement("button");
        btn.type = "button";
        btn.className = "solicitud-fila" + (solicitudActiva === s.numeroCotizacion ? " is-on" : "");

        const sem = document.createElement("span");
        sem.className = "semaforo " + (s.estado === "cotizada" ? "verde" : "rojo");
        sem.setAttribute("aria-hidden", "true");

        const main = document.createElement("div");
        main.className = "solicitud-fila-main";
        const tit = document.createElement("strong");
        tit.textContent = s.numeroCotizacion;
        const meta = document.createElement("span");
        meta.textContent = [
            s.fechaCotizacion,
            s.proveedoresCotizados,
            s.totalSolpeds + " SOLPED(s)",
            s.estado === "cotizada" ? "Cotizada" : "No cotizada"
        ].filter(Boolean).join(" · ");
        main.appendChild(tit);
        main.appendChild(meta);

        btn.appendChild(sem);
        btn.appendChild(main);
        btn.addEventListener("click", () => mostrarDetalleSolicitud(s.numeroCotizacion));
        wrap.appendChild(btn);
    });
    listaSolicitudesEl.appendChild(wrap);
}

async function mostrarDetalleSolicitud(numeroCotizacion) {
    const comprador = sel.value.trim();
    solicitudActiva = numeroCotizacion;

    try {
        const r = await fetch("/Comprador/DetalleSolicitud", {
            method: "POST",
            headers: { "Content-Type": "application/x-www-form-urlencoded" },
            body: "comprador=" + encodeURIComponent(comprador) +
                "&numeroCotizacion=" + encodeURIComponent(numeroCotizacion)
        });
        const data = await r.json();
        if (!data.valido) {
            window.SP.toast(data.mensaje || "No se pudo cargar el detalle.", true);
            return;
        }

        const primera = data.filas[0] || {};
        const cotizada = (primera.estado || "") === "cotizada";

        detalleSolicitudEl.replaceChildren();
        detalleSolicitudEl.classList.remove("is-hidden");

        const head = document.createElement("div");
        head.className = "solicitud-detalle-head";
        const sem = document.createElement("span");
        sem.className = "semaforo " + (cotizada ? "verde" : "rojo");
        const h = document.createElement("h3");
        h.textContent = numeroCotizacion + " · " + data.filas.length + " SOLPED(s)";
        head.appendChild(sem);
        head.appendChild(h);
        detalleSolicitudEl.appendChild(head);

        const info = document.createElement("p");
        info.className = "hint";
        info.textContent = [
            primera.fechaCotizacion ? "Solicitada: " + primera.fechaCotizacion : "",
            primera.proveedoresCotizados ? "Proveedores: " + primera.proveedoresCotizados : "",
            cotizada && primera.fechaRespuestaProveedor
                ? "Respondida: " + primera.fechaRespuestaProveedor
                : "Pendiente de cotización del proveedor"
        ].filter(Boolean).join(" · ");
        detalleSolicitudEl.appendChild(info);

        const tablaDatos = data.filas.map((f) => ({
            solped: f.solped,
            posicion: f.posicion,
            proveedoresCotizados: f.proveedoresCotizados,
            estado: f.estado === "cotizada" ? "Cotizada" : "No cotizada",
            fechaRespuestaProveedor: f.fechaRespuestaProveedor || "—"
        }));
        detalleSolicitudEl.appendChild(crearTabla(tablaDatos, COLUMNAS_DETALLE));
        pintarListaSolicitudes(solicitudesCache);
    } catch {
        window.SP.toast("No pude hablar con el servidor.", true);
    }
}

async function postLote(url, campos) {
    const body = Object.entries(campos)
        .map(([k, v]) => encodeURIComponent(k) + "=" + encodeURIComponent(v ?? ""))
        .join("&");
    try {
        const r = await fetch(url, {
            method: "POST",
            headers: { "Content-Type": "application/x-www-form-urlencoded" },
            body
        });
        const data = await r.json();
        if (!data.valido) {
            window.SP.toast(data.mensaje || "No se pudo actualizar.", true);
            return;
        }
        clasificacion.lotes[data.lote.loteId] = data.lote;
        window.SP.toast(data.lote.estado);
        pintarVista();
    } catch {
        window.SP.toast("No pude hablar con el servidor.", true);
    }
}

function crearTabla(datos, columnas) {
    const wrap = document.createElement("div");
    wrap.className = "table-wrap";
    const table = document.createElement("table");
    const thead = document.createElement("thead");
    const trh = document.createElement("tr");
    columnas.forEach((col) => {
        const th = document.createElement("th");
        th.textContent = window.SP.etiqueta(col);
        trh.appendChild(th);
    });
    thead.appendChild(trh);
    const tbody = document.createElement("tbody");
    if (!datos || datos.length === 0) {
        const tr = document.createElement("tr");
        const td = document.createElement("td");
        td.colSpan = columnas.length;
        td.className = "empty";
        td.textContent = "No hay registros.";
        tr.appendChild(td);
        tbody.appendChild(tr);
    } else {
        datos.forEach((fila) => {
            const tr = document.createElement("tr");
            columnas.forEach((col) => {
                const td = document.createElement("td");
                const valor = fila[col];
                td.textContent = valor == null || valor === "" ? "—" : String(valor);
                tr.appendChild(td);
            });
            tbody.appendChild(tr);
        });
    }
    table.appendChild(thead);
    table.appendChild(tbody);
    wrap.appendChild(table);
    return wrap;
}

cargarCompradores();
