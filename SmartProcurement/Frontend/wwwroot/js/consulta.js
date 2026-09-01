const promptEl = document.getElementById("prompt");
const btn = document.getElementById("btnProcesar");
const estado = document.getElementById("estado");
const trazaVacio = document.getElementById("trazaVacio");
const tablaTraza = document.getElementById("tablaTraza");
const trazaHead = document.getElementById("trazaHead");
const trazaBody = document.getElementById("trazaBody");

document.querySelectorAll("[data-ejemplo]").forEach((chip) => {
    chip.addEventListener("click", () => {
        promptEl.value = chip.getAttribute("data-ejemplo") || "";
        promptEl.focus();
    });
});

btn.addEventListener("click", async () => {
    const prompt = promptEl.value.trim();
    if (!prompt) {
        window.SP.toast("Escribe qué quieres buscar.", true);
        return;
    }

    window.SP.setLoading(btn, true, "Buscar");
    estado.textContent = "Consultando… esto puede tardar unos segundos.";

    try {
        const respuesta = await fetch("/Consulta/ProcesarConsulta", {
            method: "POST",
            headers: { "Content-Type": "application/x-www-form-urlencoded" },
            body: "prompt=" + encodeURIComponent(prompt)
        });
        const resultado = await respuesta.json();

        if (!resultado.valido) {
            window.SP.toast(resultado.mensaje || "No se pudo consultar.", true);
            estado.textContent = "";
            return;
        }

        if (resultado.vista === "trazabilidad" && resultado.traza) {
            pintarTrazabilidad(resultado.traza);
            const n = (resultado.traza.documentos || []).length;
            const hallados = (resultado.traza.documentos || []).filter((d) => d.existe).length;
            estado.textContent = n + " fila(s) · " + hallados + " con dato · " + (resultado.tipo || "") +
                (resultado.origen === "directo" ? " · directo" : " · interpretado") +
                (resultado.demo ? " · pedido_traza" : "");
            return;
        }

        window.SP.toast("Vista de tabla clásica no disponible en esta maqueta.", true);
        estado.textContent = "";
    } catch {
        window.SP.toast("No pude hablar con el servidor. ¿Está corriendo la app?", true);
        estado.textContent = "";
    } finally {
        window.SP.setLoading(btn, false, "Buscar");
    }
});

function pintarTrazabilidad(traza) {
    const columnas = traza.columnas || traza.filasEtapa || [];
    const docs = traza.documentos || [];

    trazaVacio.classList.add("is-hidden");
    tablaTraza.classList.remove("is-hidden");

    trazaHead.replaceChildren();
    trazaBody.replaceChildren();

    const head = document.createElement("tr");
    columnas.forEach((col) => {
        const th = document.createElement("th");
        if (col.tipo === "dias") {
            const esCiclo = (col.id || "").startsWith("dias_ciclo");
            th.className = "traza-col-dias" + (esCiclo ? " traza-col-ciclo" : "");
            const etiqueta = esCiclo ? (col.titulo || "Ciclo") : "Días";
            th.innerHTML =
                "<div class=\"traza-dias-head\" title=\"" + esc((col.desde || "") + " → " + (col.hasta || "")) + "\">" +
                "<span class=\"traza-dias-label\">" + esc(etiqueta) + "</span>" +
                "<small>" + esc((col.desde || "") + " → " + (col.hasta || "")) + "</small>" +
                "</div>";
        } else {
            th.className = "traza-col-etapa";
            th.innerHTML =
                "<div class=\"traza-etapa traza-etapa--head\">" +
                "<span class=\"traza-icon-wrap\">" +
                "<img class=\"traza-icon\" src=\"" + (col.icono || "") + "\" alt=\"\" />" +
                "</span>" +
                "<span class=\"traza-etapa-texto\">" + esc(col.titulo || "") + "</span>" +
                "</div>";
        }
        head.appendChild(th);
    });
    trazaHead.appendChild(head);

    if (!docs.length) {
        const tr = document.createElement("tr");
        const td = document.createElement("td");
        td.colSpan = Math.max(1, columnas.length);
        td.className = "empty";
        td.textContent = "No hay resultados para esa búsqueda.";
        tr.appendChild(td);
        trazaBody.appendChild(tr);
        return;
    }

    docs.forEach((doc) => {
        const tr = document.createElement("tr");
        if (!doc.existe) tr.classList.add("traza-sin-dato");

        columnas.forEach((col) => {
            const td = document.createElement("td");
            if (col.tipo === "dias") {
                td.className = "traza-celda traza-celda--dias" + (doc.existe ? "" : " traza-sin-dato");
                if (doc.existe) {
                    td.innerHTML = renderDias(doc.celdas?.[col.id]);
                }
            } else {
                td.className = "traza-celda" + (doc.existe ? "" : " traza-sin-dato");
                if (doc.existe) {
                    td.innerHTML = renderCelda(col.id, doc.celdas || {});
                }
            }
            tr.appendChild(td);
        });

        trazaBody.appendChild(tr);
    });
}

function renderDias(valor) {
    if (valor === null || valor === undefined || valor === "") {
        return "<span class=\"traza-dias-vacio\">—</span>";
    }
    const n = Number(valor);
    const clase = n < 0 ? "traza-dias-neg" : n === 0 ? "traza-dias-ok" : "traza-dias-val";
    return "<span class=\"" + clase + "\">" + esc(String(n)) + "</span>";
}

function renderCelda(etapaId, celdas) {
    const c = celdas[etapaId];
    if (c == null || c === "") return "";

    if (etapaId === "solped" || etapaId === "alcance" || etapaId === "informe") {
        return lineas([c.linea1, c.linea2, c.linea3]);
    }
    if (etapaId === "pedido") {
        return lineas([c.linea1, c.linea2, c.linea3, c.linea4]);
    }
    if (etapaId === "estadoPedido") {
        const ok = /concluida|liberado/i.test(String(c.linea1 || c));
        const estado = "<span class=\"" + (ok ? "estado-aprobado" : "estado-pendiente") + "\">" +
            esc(c.linea1 || c) + "</span>";
        return lineasHtml([estado, c.linea2 ? esc(c.linea2) : ""]);
    }
    if (etapaId === "ingreso") {
        return lineas([c.doc, c.fecha]);
    }
    if (etapaId === "estadoIngreso") {
        const ok = String(c.linea1 || c).toLowerCase() === "aprobado";
        const estado = "<span class=\"" + (ok ? "estado-aprobado" : "estado-pendiente") + "\">" +
            esc(c.linea1 || c) + "</span>";
        return lineasHtml([estado, c.linea2 ? esc(c.linea2) : ""]);
    }
    if (etapaId === "facturado" || etapaId === "pagado") {
        return lineas([c.linea1, c.linea2]);
    }
    return esc(c);
}

function lineas(arr) {
    return arr
        .filter((x) => x != null && String(x).trim() !== "")
        .map((x) => "<div class=\"traza-linea\">" + esc(x) + "</div>")
        .join("");
}

function lineasHtml(arr) {
    return arr
        .filter((x) => x != null && String(x).trim() !== "")
        .map((x) => "<div class=\"traza-linea\">" + x + "</div>")
        .join("");
}

function esc(s) {
    return String(s)
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll("\"", "&quot;");
}
