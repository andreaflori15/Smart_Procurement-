const CUBETA_NOMBRE = {
    contrato: "Contrato",
    exclusivo: "Exclusivo",
    menor1000: "Menor a 1000",
    licitacion: "Licitación"
};

const COLUMNAS = ["solped", "comprador", "etapa", "razon", "rango", "proveedor"];
const ETIQUETA = {
    solped: "SOLPED",
    comprador: "Comprador",
    rango: "Antigüedad",
    cubeta: "Clasificación",
    proveedor: "Proveedor",
    importeUsd: "USD",
    etapa: "Etapa",
    razon: "Por qué"
};

const EJEMPLOS = [
    "¿Qué tiene Ariana trabado más de 14 días?",
    "¿Cuántas esperan confirmación de usuario?",
    "¿Por qué no avanza la licitación?"
];

let tablero = null;
let layouts = [];
let layoutActivo = "";
let fechasHistorial = [];
let calMes = new Date();
let kpiActivo = null;
let modoVivo = true;

const kpisEl = document.getElementById("kpis");
const barrasComprador = document.getElementById("barrasComprador");
const barrasLicitacion = document.getElementById("barrasLicitacion");
const barrasRazones = document.getElementById("barrasRazones");
const embudoEl = document.getElementById("embudo");
const sugerenciasEl = document.getElementById("sugerencias");
const wrapCriticas = document.getElementById("wrapCriticas");
const pasoDetalle = document.getElementById("pasoDetalle");
const tituloDetalle = document.getElementById("tituloDetalle");
const wrapDetalle = document.getElementById("wrapDetalle");
const trazaHint = document.getElementById("trazaHint");
const askInput = document.getElementById("askInput");
const btnAsk = document.getElementById("btnAsk");
const askBox = document.getElementById("askBox");
const askRespuesta = document.getElementById("askRespuesta");
const askTabla = document.getElementById("askTabla");
const askChips = document.getElementById("askChips");
const btnLimpiar = document.getElementById("btnLimpiar");
const layoutSelect = document.getElementById("layoutSelect");
const layoutChips = document.getElementById("layoutChips");
const btnGrabarLayout = document.getElementById("btnGrabarLayout");
const btnGrabarHistorial = document.getElementById("btnGrabarHistorial");
const tabTablero = document.getElementById("tabTablero");
const tabHistorial = document.getElementById("tabHistorial");
const vistaTablero = document.getElementById("vistaTablero");
const vistaHistorial = document.getElementById("vistaHistorial");
const historialLeyenda = document.getElementById("historialLeyenda");
const calGrid = document.getElementById("calGrid");
const calTitulo = document.getElementById("calTitulo");
const calPrev = document.getElementById("calPrev");
const calNext = document.getElementById("calNext");
const historialSnapshot = document.getElementById("historialSnapshot");
const historialSnapshotTitulo = document.getElementById("historialSnapshotTitulo");

async function cargar() {
    try {
        const r = await fetch("/JefeCompra/Tablero", { method: "POST" });
        const data = await r.json();
        if (!data.valido) {
            window.SP.toast(data.mensaje || "No se pudo armar el tablero.", true);
            return;
        }
        tablero = data;
        modoVivo = true;
        if (!layoutActivo) pintarTodo();
        pintarChips();
    } catch {
        window.SP.toast("No pude hablar con el servidor.", true);
    }
}

async function cargarLayouts() {
    try {
        const r = await fetch("/JefeCompra/Layouts");
        const data = await r.json();
        if (!data.valido) return;
        layouts = data.layouts || [];
        pintarSelectorLayouts();
    } catch {
        window.SP.toast("No pude cargar los layouts.", true);
    }
}

function pintarSelectorLayouts() {
    layoutSelect.replaceChildren();
    const optVivo = document.createElement("option");
    optVivo.value = "";
    optVivo.textContent = "— Tablero en vivo —";
    layoutSelect.appendChild(optVivo);

    layouts.forEach((l) => {
        const opt = document.createElement("option");
        opt.value = l.id;
        opt.textContent = l.nombreLayout;
        layoutSelect.appendChild(opt);
    });

    layoutChips.replaceChildren();
    layouts.forEach((l) => {
        const chip = document.createElement("button");
        chip.type = "button";
        chip.className = "chip" + (layoutActivo === l.id ? " is-on" : "");
        chip.textContent = l.nombreLayout;
        chip.addEventListener("click", () => seleccionarLayout(l.id));
        layoutChips.appendChild(chip);
    });

    layoutSelect.value = layoutActivo;
    actualizarControlesLayout();
}

async function seleccionarLayout(id) {
    layoutActivo = id || "";
    layoutSelect.value = layoutActivo;
    layoutChips.querySelectorAll(".chip").forEach((c) => c.classList.remove("is-on"));
    layoutChips.querySelectorAll(".chip").forEach((chip) => {
        if (chip.textContent === layouts.find((l) => l.id === id)?.nombreLayout) {
            chip.classList.add("is-on");
        }
    });

    if (!layoutActivo) {
        modoVivo = true;
        fechasHistorial = [];
        await cargar();
        irTab("tablero");
        actualizarControlesLayout();
        return;
    }

    modoVivo = false;
    const layout = layouts.find((l) => l.id === layoutActivo);
    fechasHistorial = layout?.fechas || [];

    try {
        const r = await fetch("/JefeCompra/Layouts");
        const data = await r.json();
        const full = (data.layouts || []).find((l) => l.id === layoutActivo);
        if (full?.prompt) askInput.value = full.prompt;
    } catch { /* ignore */ }

    const det = await fetch(`/JefeCompra/Historial?layoutId=${encodeURIComponent(layoutActivo)}`);
    const hist = await det.json();
    if (hist.valido) fechasHistorial = hist.fechas || [];

    await aplicarLayoutGuardado(layoutActivo);
    actualizarControlesLayout();
}

async function aplicarLayoutGuardado(id) {
    try {
        const r = await fetch(`/JefeCompra/Layout?id=${encodeURIComponent(id)}`);
        const data = await r.json();
        if (!data.valido || !data.layout?.esquema) {
            window.SP.toast("No pude cargar el esquema del layout.", true);
            return;
        }
        tablero = esquemaATablero(data.layout.esquema);
        if (data.layout.prompt) askInput.value = data.layout.prompt;
        pintarTodo();
    } catch {
        window.SP.toast("No pude cargar el layout.", true);
    }
}

function esquemaATablero(esquema) {
    const porRango = esquema.porRango || [];
    return {
        valido: true,
        kpis: porRango.map((r, i) => ({
            id: "rango-" + i,
            titulo: r.rango,
            valor: String(r.count ?? 0),
            tono: r.edad === "bad" ? "danger" : r.edad === "mid" ? "warn" : "ok",
            filtro: "todas",
            flecha: "→",
            vs: "guardado",
            bueno: r.edad === "ok",
            hint: "SOLPED por rango de fecha"
        })),
        compradores: esquema.compradores || [],
        embudo: (esquema.trazabilidad || []).map((t) => ({
            id: t.id,
            titulo: t.titulo,
            count: t.count
        })),
        razones: [],
        sugerencias: esquema.sugerencias || [],
        criticas: esquema.criticas || [],
        licitacion: esquema.licitacion || [],
        items: tablero?.items || []
    };
}

function actualizarControlesLayout() {
    const hayLayout = Boolean(layoutActivo);
    btnGrabarHistorial.classList.toggle("is-hidden", !hayLayout);
    btnGrabarHistorial.disabled = !hayLayout;

    const tieneHist = hayLayout && fechasHistorial.length > 0;
    tabHistorial.classList.toggle("is-hidden", !tieneHist);
}

function irTab(tab) {
    const esHist = tab === "historial";
    tabTablero.classList.toggle("is-on", !esHist);
    tabHistorial.classList.toggle("is-on", esHist);
    vistaTablero.classList.toggle("is-hidden", esHist);
    vistaHistorial.classList.toggle("is-hidden", !esHist);
    if (esHist) {
        calMes = fechasHistorial.length
            ? new Date(fechasHistorial[fechasHistorial.length - 1] + "T12:00:00")
            : new Date();
        pintarCalendario();
    }
}

async function grabarLayout() {
    const nombre = window.prompt("Nombre del layout (máx. 50 caracteres):", askInput.value.trim().slice(0, 50) || "Mi layout");
    if (!nombre?.trim()) return;

    try {
        const r = await fetch("/JefeCompra/GrabarLayout", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
                nombreLayout: nombre.trim(),
                prompt: askInput.value.trim()
            })
        });
        const data = await r.json();
        if (!data.valido) {
            window.SP.toast(data.mensaje || "No se pudo grabar.", true);
            return;
        }
        window.SP.toast("Layout guardado: " + nombre.trim());
        layoutActivo = data.id || layoutActivo;
        await cargarLayouts();
        await seleccionarLayout(data.id);
    } catch {
        window.SP.toast("Error al grabar layout.", true);
    }
}

async function grabarHistorial() {
    if (!layoutActivo) {
        window.SP.toast("Selecciona un layout primero.", true);
        return;
    }

    try {
        const r = await fetch("/JefeCompra/GrabarHistorial", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ layoutId: layoutActivo })
        });
        const data = await r.json();
        if (!data.valido) {
            window.SP.toast(data.mensaje || "No se pudo grabar.", true);
            return;
        }
        window.SP.toast("Historial guardado para " + (data.fecha || "hoy"));
        await cargarLayouts();
        fechasHistorial = layouts.find((l) => l.id === layoutActivo)?.fechas || [];
        if (data.fecha && !fechasHistorial.includes(data.fecha)) {
            fechasHistorial.push(data.fecha);
            fechasHistorial.sort();
        }
        actualizarControlesLayout();
    } catch {
        window.SP.toast("Error al grabar historial.", true);
    }
}

function pintarCalendario() {
    const y = calMes.getFullYear();
    const m = calMes.getMonth();
    const meses = ["Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio", "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre"];
    calTitulo.textContent = meses[m] + " " + y;

    const primerDia = new Date(y, m, 1);
    const ultimoDia = new Date(y, m + 1, 0);
    const offset = (primerDia.getDay() + 6) % 7;

    calGrid.replaceChildren();
    ["L", "M", "X", "J", "V", "S", "D"].forEach((d) => {
        const h = document.createElement("span");
        h.className = "cal-head";
        h.textContent = d;
        calGrid.appendChild(h);
    });

    for (let i = 0; i < offset; i++) {
        const blank = document.createElement("span");
        blank.className = "cal-day cal-day--blank";
        calGrid.appendChild(blank);
    }

    for (let dia = 1; dia <= ultimoDia.getDate(); dia++) {
        const fecha = `${y}-${String(m + 1).padStart(2, "0")}-${String(dia).padStart(2, "0")}`;
        const btn = document.createElement("button");
        btn.type = "button";
        btn.className = "cal-day";
        btn.textContent = String(dia);
        if (fechasHistorial.includes(fecha)) {
            btn.classList.add("cal-day--has");
            btn.addEventListener("click", () => verHistorialDia(fecha));
        } else {
            btn.disabled = true;
        }
        calGrid.appendChild(btn);
    }

    historialLeyenda.textContent = fechasHistorial.length
        ? "Días en verde tienen snapshot. Pulsa uno para ver el tablero histórico."
        : "Este layout aún no tiene historial grabado.";
}

async function verHistorialDia(fecha) {
    try {
        const r = await fetch(`/JefeCompra/HistorialDia?layoutId=${encodeURIComponent(layoutActivo)}&fecha=${encodeURIComponent(fecha)}`);
        const data = await r.json();
        if (!data.valido) {
            window.SP.toast(data.mensaje || "Sin datos.", true);
            return;
        }
        const t = esquemaATablero(data.esquema);
        const nombre = layouts.find((l) => l.id === layoutActivo)?.nombreLayout || layoutActivo;
        historialSnapshotTitulo.textContent = nombre + " · " + fecha;
        pintarSnapshotHistorial(t);
        historialSnapshot.classList.remove("is-hidden");
    } catch {
        window.SP.toast("No pude cargar el snapshot.", true);
    }
}

function pintarSnapshotHistorial(t) {
    pintarKpisEn(document.getElementById("kpisHist"), t);
    pintarEmbudoEn(document.getElementById("embudoHist"), t);
    pintarBarrasCompradorEn(document.getElementById("barrasCompradorHist"), t);
    pintarCriticasEn(document.getElementById("wrapCriticasHist"), t);
    pintarSugerenciasEn(document.getElementById("sugerenciasHist"), t);
}

function pintarTodo() {
    pintarKpis();
    pintarEmbudo();
    pintarRazones();
    pintarBarrasComprador();
    pintarSugerencias();
    pintarCriticas();
    pintarLicitacion();
}

function pintarChips() {
    askChips.replaceChildren();
    EJEMPLOS.forEach((t) => {
        const b = document.createElement("button");
        b.type = "button";
        b.className = "chip";
        b.textContent = t;
        b.addEventListener("click", () => {
            askInput.value = t;
            preguntar();
        });
        askChips.appendChild(b);
    });
}

btnAsk.addEventListener("click", preguntar);
btnLimpiar.addEventListener("click", limpiar);
btnGrabarLayout.addEventListener("click", grabarLayout);
btnGrabarHistorial.addEventListener("click", grabarHistorial);
layoutSelect.addEventListener("change", () => seleccionarLayout(layoutSelect.value));
tabTablero.addEventListener("click", () => irTab("tablero"));
tabHistorial.addEventListener("click", () => irTab("historial"));
calPrev.addEventListener("click", () => { calMes.setMonth(calMes.getMonth() - 1); pintarCalendario(); });
calNext.addEventListener("click", () => { calMes.setMonth(calMes.getMonth() + 1); pintarCalendario(); });
askInput.addEventListener("keydown", (e) => {
    if (e.key === "Enter") preguntar();
});

function preguntar() {
    const prompt = askInput.value.trim();
    if (!prompt) {
        window.SP.toast("Escribe una pregunta.", true);
        return;
    }
    if (!tablero?.items?.length && !modoVivo) {
        window.SP.toast("En modo layout no hay detalle por SOLPED. Usa tablero en vivo para preguntas.", true);
        return;
    }
    if (!tablero?.items) {
        window.SP.toast("Espera a que cargue el tablero.", true);
        return;
    }

    const hit = filtrarPregunta(prompt, tablero.items);
    askBox.classList.remove("is-hidden");
    btnLimpiar.classList.remove("is-hidden");
    askRespuesta.textContent = redactar(prompt, hit, tablero.items.length);
    askTabla.replaceChildren(tabla(hit.slice(0, 20)));
    askBox.scrollIntoView({ behavior: "smooth", block: "nearest" });
}

function limpiar() {
    askInput.value = "";
    askRespuesta.textContent = "";
    askTabla.replaceChildren();
    askBox.classList.add("is-hidden");
    pasoDetalle.classList.add("is-hidden");
    wrapDetalle.replaceChildren();
    trazaHint.textContent = "";
    kpiActivo = null;
    kpisEl.querySelectorAll(".kpi").forEach((el) => el.classList.remove("is-on"));
    btnLimpiar.classList.add("is-hidden");
}

function filtrarPregunta(prompt, items) {
    const low = prompt.toLowerCase();
    let q = items.slice();
    const nums = prompt.match(/\d{6,12}/g) || [];
    if (nums.length) q = q.filter((i) => nums.includes(i.solped));

    (tablero.compradores || []).forEach((c) => {
        const pila = (c.nombre || "").split(" ")[0];
        if (pila.length >= 4 && low.includes(pila.toLowerCase())) {
            q = q.filter((i) => i.comprador === c.nombre);
        }
    });

    if (/22|cr[ií]tica|vencid/.test(low)) q = q.filter((i) => (i.rango || "").includes("22"));
    else if (/14|antigu|trabad/.test(low)) q = q.filter((i) => !(i.rango || "").includes("01"));

    if (low.includes("licit")) q = q.filter((i) => i.cubeta === "licitacion");
    if (/esperando|oferta/.test(low)) q = q.filter((i) => i.etapaId === "esperar");
    if (/confirm|usuario/.test(low)) q = q.filter((i) => i.etapaId === "confirmar");
    if (low.includes("compar")) q = q.filter((i) => i.etapaId === "comparar");
    if (/contrato|\bsap\b/.test(low)) q = q.filter((i) => i.etapaId === "sap" || i.cubeta === "contrato");
    if (low.includes("cotiz") && !low.includes("esperando")) q = q.filter((i) => i.etapaId === "cotizar" || i.etapaId === "esperar");
    return q;
}

function redactar(prompt, hit, total) {
    if (!hit.length) {
        return "No encontré SOLPEDs con eso. Prueba con un comprador, una SOLPED o una etapa (esperando oferta, confirmar usuario, licitación).";
    }
    const nums = prompt.match(/\d{6,12}/g) || [];
    if (nums.length === 1 && hit.length === 1) {
        const i = hit[0];
        return "La SOLPED " + i.solped + " de " + i.comprador + " está en «" + i.etapa + "». Razón: " + i.razon + ". " + (i.trazabilidad || "");
    }
    const etapas = {};
    const razones = {};
    hit.forEach((i) => {
        etapas[i.etapa] = (etapas[i.etapa] || 0) + 1;
        razones[i.razon] = (razones[i.razon] || 0) + 1;
    });
    const topE = Object.entries(etapas).sort((a, b) => b[1] - a[1])[0];
    const topR = Object.entries(razones).sort((a, b) => b[1] - a[1])[0];
    return "Encontré " + hit.length + " SOLPED(s) de " + total + " pendientes. La mayoría está en «" + topE[0] + "» (" + topE[1] + "). Motivo más frecuente: " + topR[0] + ".";
}

function pintarKpis() { pintarKpisEn(kpisEl, tablero); }
function pintarEmbudo() { pintarEmbudoEn(embudoEl, tablero); }
function pintarBarrasComprador() { pintarBarrasCompradorEn(barrasComprador, tablero); }
function pintarCriticas() { pintarCriticasEn(wrapCriticas, tablero); }
function pintarSugerencias() { pintarSugerenciasEn(sugerenciasEl, tablero); }

function pintarKpisEn(el, t) {
    el.replaceChildren();
    (t?.kpis || []).forEach((k) => {
        const btn = document.createElement("button");
        btn.type = "button";
        btn.className = "kpi kpi-" + (k.tono || "brand");
        btn.title = k.hint || "";
        const vsClass = k.bueno ? "is-good" : "is-bad";
        btn.innerHTML =
            "<span class=\"kpi-title\">" + k.titulo + "</span>" +
            "<strong>" + (k.valor ?? "—") + "</strong>" +
            "<span class=\"kpi-vs " + vsClass + "\">" + (k.flecha || "") + " " + (k.vs || "") + "</span>";
        if (el === kpisEl && t?.items?.length) {
            btn.addEventListener("click", () => {
                kpiActivo = k.id;
                kpisEl.querySelectorAll(".kpi").forEach((x) => x.classList.remove("is-on"));
                btn.classList.add("is-on");
                btnLimpiar.classList.remove("is-hidden");
                mostrarDetalle(k.filtro, k.titulo);
            });
        }
        el.appendChild(btn);
    });
}

function pintarEmbudoEn(el, t) {
    el.replaceChildren();
    (t?.embudo || []).forEach((p, i) => {
        if (i > 0) {
            const flecha = document.createElement("span");
            flecha.className = "embudo-flecha";
            flecha.textContent = "→";
            el.appendChild(flecha);
        }
        const btn = document.createElement("button");
        btn.type = "button";
        btn.className = "embudo-paso";
        btn.innerHTML = "<strong>" + p.count + "</strong><span>" + p.titulo + "</span>";
        if (el === embudoEl && t?.items?.length) {
            btn.addEventListener("click", () => mostrarDetalle("etapa:" + p.id, p.titulo));
        }
        el.appendChild(btn);
    });
}

function pintarRazones() {
    const filas = tablero?.razones || [];
    const max = Math.max(1, ...filas.map((c) => c.count || 0));
    barrasRazones.replaceChildren();
    filas.forEach((c) => {
        const row = document.createElement("button");
        row.type = "button";
        row.className = "bar-row";
        const w = ((c.count || 0) / max * 100).toFixed(1) + "%";
        row.innerHTML =
            "<span class=\"bar-label\">" + c.titulo + "</span>" +
            "<span class=\"bar\"><i class=\"seg bad\" style=\"width:" + w + "\"></i></span>" +
            "<span class=\"bar-n\">" + c.count + "</span>";
        row.addEventListener("click", () => mostrarDetalle("razon:" + c.titulo, c.titulo));
        barrasRazones.appendChild(row);
    });
}

function pintarBarrasCompradorEn(el, t) {
    const filas = t?.compradores || [];
    const max = Math.max(1, ...filas.map((c) => c.total || 0));
    el.replaceChildren();
    filas.forEach((c) => {
        const row = document.createElement("button");
        row.type = "button";
        row.className = "bar-row";
        const fill = ((c.total || 0) / max * 100).toFixed(1);
        const part = (n) => (!c.total ? "0%" : ((n || 0) / c.total * 100).toFixed(1) + "%");
        row.innerHTML =
            "<span class=\"bar-label\">" + c.nombre + "</span>" +
            "<span class=\"bar\"><span class=\"bar-fill\" style=\"width:" + fill + "%\">" +
            "<i class=\"seg ok\" style=\"width:" + part(c.alDia) + "\"></i>" +
            "<i class=\"seg mid\" style=\"width:" + part(c.media) + "\"></i>" +
            "<i class=\"seg bad\" style=\"width:" + part(c.critica) + "\"></i>" +
            "</span></span>" +
            "<span class=\"bar-n\">" + c.total + "</span>";
        if (el === barrasComprador && t?.items?.length) {
            row.addEventListener("click", () => mostrarDetalle("comprador:" + c.nombre, c.nombre));
        }
        el.appendChild(row);
    });
}

function pintarLicitacion() {
    const filas = tablero?.licitacion || [];
    const max = Math.max(1, ...filas.map((c) => c.count || 0));
    barrasLicitacion.replaceChildren();
    if (filas.length === 0) {
        barrasLicitacion.innerHTML = "<p class=\"empty\">No hay SOLPEDs en licitación.</p>";
        return;
    }
    filas.forEach((c) => {
        const row = document.createElement("button");
        row.type = "button";
        row.className = "bar-row";
        const w = ((c.count || 0) / max * 100).toFixed(1) + "%";
        row.innerHTML =
            "<span class=\"bar-label\">" + c.proveedor + "</span>" +
            "<span class=\"bar\"><i class=\"seg mid\" style=\"width:" + w + "\"></i></span>" +
            "<span class=\"bar-n\">" + c.count + "</span>";
        row.addEventListener("click", () => mostrarDetalle("grupo:" + c.proveedor, c.proveedor));
        barrasLicitacion.appendChild(row);
    });
}

function pintarSugerenciasEn(el, t) {
    el.replaceChildren();
    (t?.sugerencias || []).forEach((txt) => {
        const li = document.createElement("li");
        li.textContent = txt;
        el.appendChild(li);
    });
}

function pintarCriticasEn(el, t) {
    el.replaceChildren(tabla(t?.criticas || []));
}

function pintarBarrasComprador() { pintarBarrasCompradorEn(barrasComprador, tablero); }

function mostrarDetalle(filtro, titulo) {
    const items = (tablero.items || []).filter((i) => pasaFiltro(i, filtro));
    tituloDetalle.textContent = titulo + " · " + items.length;
    const uno = items[0];
    trazaHint.textContent = items.length === 1 && uno?.trazabilidad ? uno.trazabilidad : "";
    wrapDetalle.replaceChildren(tabla(items.slice(0, 40)));
    pasoDetalle.classList.remove("is-hidden");
    btnLimpiar.classList.remove("is-hidden");
    pasoDetalle.scrollIntoView({ behavior: "smooth", block: "start" });
}

function pasaFiltro(i, filtro) {
    if (filtro === "todas") return true;
    if (filtro === "criticas") return (i.rango || "").includes("22");
    if (filtro === "licitacion") return i.cubeta === "licitacion";
    if (filtro === "accion") return i.etapaId === "clasificar" || i.etapaId === "cotizar" || i.etapaId === "sap";
    if (filtro === "espera") return i.etapaId === "esperar" || i.etapaId === "confirmar";
    if (filtro === "contrato") return i.cubeta === "contrato" || i.etapaId === "sap";
    if (filtro.startsWith("comprador:")) return i.comprador === filtro.slice(10);
    if (filtro.startsWith("cubeta:")) return i.cubeta === filtro.slice(7);
    if (filtro.startsWith("grupo:")) return i.grupo === filtro.slice(6);
    if (filtro.startsWith("etapa:")) return i.etapaId === filtro.slice(6);
    if (filtro.startsWith("razon:")) return i.razon === filtro.slice(6);
    return true;
}

function tabla(filas) {
    const wrap = document.createElement("div");
    wrap.className = "table-wrap";
    const table = document.createElement("table");
    const thead = document.createElement("thead");
    const trh = document.createElement("tr");
    COLUMNAS.forEach((col) => {
        const th = document.createElement("th");
        th.textContent = ETIQUETA[col] || col;
        trh.appendChild(th);
    });
    thead.appendChild(trh);
    const tbody = document.createElement("tbody");
    if (!filas.length) {
        const tr = document.createElement("tr");
        const td = document.createElement("td");
        td.colSpan = COLUMNAS.length;
        td.className = "empty";
        td.textContent = "No hay registros en este recorte.";
        tr.appendChild(td);
        tbody.appendChild(tr);
    } else {
        filas.forEach((fila) => {
            const tr = document.createElement("tr");
            tr.title = fila.trazabilidad || "";
            COLUMNAS.forEach((col) => {
                const td = document.createElement("td");
                let valor = fila[col];
                if (col === "cubeta") valor = CUBETA_NOMBRE[valor] || valor;
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

cargarLayouts().then(() => cargar());
