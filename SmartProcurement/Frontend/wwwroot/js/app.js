window.SP = {
    toast(mensaje, error) {
        const el = document.getElementById("toast");
        if (!el) {
            alert(mensaje);
            return;
        }
        el.textContent = mensaje;
        el.classList.toggle("is-error", Boolean(error));
        el.classList.add("is-on");
        clearTimeout(window.SP._toastTimer);
        window.SP._toastTimer = setTimeout(() => el.classList.remove("is-on"), 4200);
    },

    setLoading(boton, cargando, textoNormal) {
        if (!boton) return;
        boton.disabled = cargando;
        boton.textContent = cargando ? "Cargando…" : textoNormal;
    },

    etiqueta(nombre) {
        const mapa = {
            NSOLPED: "SOLPED",
            NPedido: "Pedido",
            NPos: "Posición",
            NPOs: "Posición",
            Razon_Social: "Proveedor",
            RazSocial: "Proveedor",
            Estado_Ped: "Estado",
            "Tipo de compra Denominacion": "Tipo de compra",
            COMPRADOR_DESC: "Comprador",
            COMPRADOR_EMAIL: "Email comprador",
            RANGO_FECHA: "Antigüedad",
            Texto_Breve: "Descripción",
            Cantidad_Solicitada: "Cantidad",
            Accion_Usuario_Fecha: "Fecha acción",
            Email: "Email proveedor",
            Pro: "Proveedor SAP",
            Centro: "Centro",
            Material: "Material",
            Unidad_Medida: "Unidad",
            UNIDAD_MEDIDA: "Unidad",
            Fte: "Fuente",
            NumPed: "Pedido hist.",
            FecPed: "Fecha pedido",
            PrecioNeto: "Precio neto",
            TipoCompra_Den: "Tipo de compra",
            IMPORTE: "Importe",
            MONEDA: "Moneda",
            ImporteUsd: "Importe USD",
            Cubeta: "Clasificación",
            Tratamiento: "Tratamiento",
            Contrato: "Contrato",
            Grupo: "Grupo",
            LoteNombre: "Lote",
            Estado: "Estado",
            solped: "SOLPED",
            posicion: "Posición",
            proveedoresCotizados: "Proveedores",
            estado: "Estado cotización",
            fechaRespuestaProveedor: "Fecha respuesta",
        };
        return mapa[nombre] || nombre.replaceAll("_", " ");
    },

    llenarTabla(tbodyId, datos) {
        const tbody = document.getElementById(tbodyId);
        if (!tbody) return;
        tbody.replaceChildren();

        if (!datos || datos.length === 0) {
            const tr = document.createElement("tr");
            const td = document.createElement("td");
            td.colSpan = 12;
            td.className = "empty";
            td.textContent = "No encontré registros con esa búsqueda.";
            tr.appendChild(td);
            tbody.appendChild(tr);
            return;
        }

        const columnas = Object.keys(datos[0]);
        const theadRow = tbody.closest("table")?.querySelector("thead tr");
        if (theadRow) {
            theadRow.replaceChildren();
            columnas.forEach((nombre) => {
                const th = document.createElement("th");
                th.textContent = window.SP.etiqueta(nombre);
                theadRow.appendChild(th);
            });
        }

        datos.forEach((fila) => {
            const tr = document.createElement("tr");
            columnas.forEach((nombre) => {
                const td = document.createElement("td");
                const valor = fila[nombre];
                td.textContent = valor == null ? "—" : String(valor);
                tr.appendChild(td);
            });
            tbody.appendChild(tr);
        });
    }
};

document.getElementById("btnMenu")?.addEventListener("click", () => {
    document.getElementById("sidebar")?.classList.toggle("is-open");
});
