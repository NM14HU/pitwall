/* =========================================================
   PIT WALL · app.js
   Lee data/noticias.json y data/mundial.json y pinta la página.
   El generador (más adelante) solo tendrá que crear esos dos JSON.
   ========================================================= */

// Secciones de la web. "codigo" enlaza Alonso y Sainz con la clasificación.
const SECCIONES = [
  { id: "F1",     nombre: "Fórmula 1", color: "var(--c-f1)" },
  { id: "Alonso", nombre: "Alonso",    color: "var(--c-alonso)", codigo: "ALO", completo: "Fernando Alonso" },
  { id: "Sainz",  nombre: "Sainz",     color: "var(--c-sainz)",  codigo: "SAI", completo: "Carlos Sainz" },
  { id: "F2",     nombre: "Fórmula 2", color: "var(--c-f2)" },
  { id: "F3",     nombre: "Fórmula 3", color: "var(--c-f3)" },
  { id: "FE",     nombre: "Fórmula E", color: "var(--c-fe)" }
];

const ZONA = "Europe/Madrid";
const estado = {
  noticias: [], mundial: null, campeonatos: [], filtro: "todo", tabla: "pilotos", idioma: "es",
  pestanaMundial: "F1", tarjetaAbierta: null, ultimaCarga: 0, huella: ""
};

const $ = (sel) => document.querySelector(sel);
const seccion = (id) => SECCIONES.find((s) => s.id === id);

/* ---------- Utilidades ---------- */

// Evita que un titular con caracteres raros rompa el HTML
function esc(texto = "") {
  return String(texto).replace(/[&<>"']/g, (c) =>
    ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
}

const relativo = new Intl.RelativeTimeFormat("es", { numeric: "auto" });
function haceCuanto(fechaIso) {
  const seg = (new Date(fechaIso) - Date.now()) / 1000;
  const pasos = [[60, "second"], [60, "minute"], [24, "hour"], [7, "day"], [4.35, "week"], [12, "month"]];
  let valor = seg;
  for (const [limite, unidad] of pasos) {
    if (Math.abs(valor) < limite) return relativo.format(Math.round(valor), unidad);
    valor /= limite;
  }
  return relativo.format(Math.round(valor), "year");
}

const fmtDia  = new Intl.DateTimeFormat("es-ES", { day: "numeric", month: "short", timeZone: ZONA });
const fmtHora = new Intl.DateTimeFormat("es-ES", { weekday: "short", hour: "2-digit", minute: "2-digit", timeZone: ZONA });
const fmtAct  = new Intl.DateTimeFormat("es-ES", { day: "numeric", month: "long", hour: "2-digit", minute: "2-digit", timeZone: ZONA });

/* ---------- Piezas de noticias ---------- */

// Algunas webs bloquean sus fotos si se ven desde otra página; sin «referrer» no saben desde dónde se piden.
// Si aun así una foto falla, se cambia por la imagen genérica.
const ATRIBUTOS_FOTO = `referrerpolicy="no-referrer" onerror="this.onerror=null;this.src='img/sin-foto.svg'"`;
// En las tarjetas, si la foto falla se quita y la tarjeta pasa a ser de solo texto
const ATRIBUTOS_FOTO_TARJETA = `referrerpolicy="no-referrer" onerror="this.onerror=null;this.closest('.tarjeta').classList.add('sin-foto');this.remove()"`;

const tieneFoto = (n) => Boolean(n.imagen) && !n.imagen.endsWith("sin-foto.svg");

function etiquetasHtml(n) {
  return `<div class="etiquetas">${n.etiquetas.map((id) => {
    const s = seccion(id);
    return s ? `<span class="etiqueta" style="--color:${s.color}">${esc(s.nombre)}</span>` : "";
  }).join("")}</div>`;
}

function metaHtml(n) {
  const en = n.idioma === "en" ? `<span class="idioma-en" title="Noticia en inglés">EN</span>` : "";
  return `<p class="meta"><span class="medio">${esc(n.medio)}</span><time datetime="${esc(n.fecha)}">${haceCuanto(n.fecha)}</time>${en}</p>`;
}

// Noticias que se ven según el idioma elegido (las que no indican idioma se consideran en español)
function visibles() {
  return estado.idioma === "todas"
    ? estado.noticias
    : estado.noticias.filter((n) => (n.idioma || "es") === "es");
}

function colorPrincipal(n) {
  const s = seccion(n.etiquetas[0]);
  return s ? s.color : "var(--linea)";
}

function tarjeta(n) {
  const foto = tieneFoto(n);
  return `
    <a class="tarjeta${foto ? "" : " sin-foto"}" href="${esc(n.url)}" target="_blank" rel="noopener" style="--color:${colorPrincipal(n)}">
      ${foto ? `<img class="foto" src="${esc(n.imagen)}" alt="" loading="lazy" ${ATRIBUTOS_FOTO_TARJETA}>` : ""}
      <div class="cuerpo">
        ${etiquetasHtml(n)}
        <h3>${esc(n.titulo)}</h3>
        ${metaHtml(n)}
      </div>
    </a>`;
}

function destacada(n) {
  return `
    <a class="destacada" href="${esc(n.url)}" target="_blank" rel="noopener">
      <img class="foto" src="${esc(n.imagen)}" alt="" ${ATRIBUTOS_FOTO}>
      <div class="cuerpo">
        ${etiquetasHtml(n)}
        <h2>${esc(n.titulo)}</h2>
        ${n.resumen ? `<p class="resumen">${esc(n.resumen)}</p>` : ""}
        ${metaHtml(n)}
      </div>
    </a>`;
}

/* ---------- Vistas ---------- */

function vistaPortada() {
  // La destacada es la noticia de F1 más reciente que tenga foto
  const todas = visibles();
  const primera = todas.find((n) => tieneFoto(n) && n.etiquetas.includes("F1"))
               || todas.find(tieneFoto)
               || todas[0];
  const resto = todas.filter((n) => n !== primera);
  if (!primera) return `<p class="vacio">Todavía no hay noticias. Vuelve en unos minutos.</p>`;

  let html = destacada(primera);
  html += `
    <section class="seccion">
      <div class="seccion-cab"><h2>Lo último</h2></div>
      <div class="rejilla">${resto.slice(0, 6).map(tarjeta).join("")}</div>
    </section>`;

  for (const s of SECCIONES) {
    const suyas = visibles().filter((n) => n.etiquetas.includes(s.id)).slice(0, 3);
    if (!suyas.length) continue;
    html += `
      <section class="seccion">
        <div class="seccion-cab" style="--color:${s.color}">
          <h2>${esc(s.nombre)}</h2>
          <button class="ver-todas" data-ir="${s.id}">Ver todas</button>
        </div>
        <div class="fila">${suyas.map(tarjeta).join("")}</div>
      </section>`;
  }
  return html;
}

function fichaPiloto(s) {
  const p = estado.mundial?.pilotos?.find((x) => x.codigo === s.codigo);
  if (!p) return "";
  return `
    <div class="ficha" style="--color:${s.color}">
      <div class="ficha-pos">${p.pos}<small>º</small></div>
      <h2>${esc(s.completo)}</h2>
      <p class="ficha-datos">
        <span><strong>${p.puntos}</strong> puntos</span>
        <span>${esc(p.equipo)}</span>
        ${p.ultima ? `<span>Última carrera: <strong>${esc(p.ultima)}</strong></span>` : ""}
      </p>
    </div>`;
}

function vistaSeccion(id) {
  const s = seccion(id);
  const suyas = visibles().filter((n) => n.etiquetas.includes(id));
  let html = s.codigo ? fichaPiloto(s) : tarjetaCampeonato();
  html += `
    <section class="seccion">
      <div class="seccion-cab" style="--color:${s.color}"><h2>${esc(s.nombre)}</h2></div>
      ${suyas.length
        ? `<div class="rejilla">${suyas.map(tarjeta).join("")}</div>`
        : `<p class="vacio">No hay noticias de ${esc(s.nombre)} en este momento.</p>`}
    </section>`;
  return html;
}

function pintarPrincipal() {
  const principal = $("#principal");
  principal.innerHTML = seccion(estado.filtro) ? vistaSeccion(estado.filtro) : vistaPortada();
  actualizarCuenta();
}

/* ---------- Filtros ---------- */

function pintarFiltros() {
  const botones = [
    `<button class="filtro filtro-todo" data-filtro="todo">Todo</button>`,
    `<button class="filtro filtro-mundial" data-filtro="mundial" style="--color:var(--rojo)">Mundial</button>`,
    ...SECCIONES.map((s) => `<button class="filtro" data-filtro="${s.id}" style="--color:${s.color}">${esc(s.nombre)}</button>`)
  ];
  $("#filtros").innerHTML = botones.join("");
  marcarFiltro();
}

function marcarFiltro() {
  document.querySelectorAll(".filtro").forEach((b) =>
    b.setAttribute("aria-pressed", String(b.dataset.filtro === estado.filtro)));
  document.body.classList.toggle("ver-mundial", estado.filtro === "mundial");
}

function cambiarFiltro(id) {
  estado.filtro = id;
  marcarFiltro();
  estado.tarjetaAbierta = null;
  pintarPrincipal();
  pintarLateral();
  history.replaceState(null, "", id === "todo" ? location.pathname : `#${id}`);
  window.scrollTo({ top: 0, behavior: "smooth" });
}

document.addEventListener("click", (e) => {
  const filtro = e.target.closest("[data-filtro]");
  if (filtro) return cambiarFiltro(filtro.dataset.filtro);
  const ir = e.target.closest("[data-ir]");
  if (ir) return cambiarFiltro(ir.dataset.ir);
  const idioma = e.target.closest("[data-idioma]");
  if (idioma) return cambiarIdioma(idioma.dataset.idioma);
  const pestana = e.target.closest("[data-tabla]");
  if (pestana) {
    estado.tabla = pestana.dataset.tabla;
    pintarLateral();
    if (estado.tarjetaAbierta) pintarPrincipal();
    return;
  }
  const campeonato = e.target.closest("[data-campeonato]");
  if (campeonato) {
    estado.pestanaMundial = campeonato.dataset.campeonato;
    return pintarLateral();
  }
  const desplegar = e.target.closest("[data-desplegar]");
  if (desplegar) {
    const id = desplegar.dataset.desplegar;
    estado.tarjetaAbierta = estado.tarjetaAbierta === id ? null : id;
    pintarPrincipal();
    if (!estado.tarjetaAbierta) $(".tarjeta-campeonato")?.scrollIntoView({ block: "nearest" });
  }
});

/* ---------- Idioma de las noticias ---------- */

function cambiarIdioma(idioma, guardar = true) {
  estado.idioma = idioma === "todas" ? "todas" : "es";
  document.querySelectorAll("[data-idioma]").forEach((b) =>
    b.setAttribute("aria-pressed", String(b.dataset.idioma === estado.idioma)));
  if (guardar) { try { localStorage.setItem("idioma", estado.idioma); } catch {} }
  if (estado.noticias.length) pintarPrincipal();
}

/* ---------- Campeonatos: F1 (mundial.json) y F2/F3 (campeonatos.json) ---------- */

const NOMBRES_CAMPEONATO = { F1: "Fórmula 1", F2: "Fórmula 2", F3: "Fórmula 3", FE: "Fórmula E" };
const otroCampeonato = (id) => estado.campeonatos.find((c) => c.id === id);

// Campeonatos con datos, en el orden de las pestañas
function campeonatosDisponibles() {
  return [...(estado.mundial ? ["F1"] : []), ...estado.campeonatos.map((c) => c.id)];
}

// Qué campeonato acompaña a cada sección: F2, F3 y FE el suyo (aunque aún no tenga datos); el resto, la F1
function campeonatoDeSeccion(filtro) {
  return ["F2", "F3", "FE"].includes(filtro) ? filtro : "F1";
}

const colorCampeonato = (id) => seccion(id)?.color || "var(--c-f1)";

// "27–29 nov" o "30 oct – 1 nov"
const fmtDiaMes = new Intl.DateTimeFormat("es-ES", { day: "numeric", month: "short" });
function rangoFechas(inicio, fin) {
  const a = new Date(`${inicio}T12:00:00`), b = new Date(`${fin}T12:00:00`);
  return a.getMonth() === b.getMonth()
    ? `${a.getDate()}–${fmtDiaMes.format(b)}`
    : `${fmtDiaMes.format(a)} – ${fmtDiaMes.format(b)}`;
}

/* --- Cuenta atrás: sirve para cualquier caja con data-fecha --- */

const cuentaHtml = (fechaIso, textoFin) =>
  `<div class="cuenta-js" data-fecha="${esc(fechaIso)}" data-fin="${esc(textoFin)}"></div>`;

function actualizarCuenta() {
  const dos = (n) => String(n).padStart(2, "0");
  document.querySelectorAll(".cuenta-js").forEach((caja) => {
    let resto = Math.floor((new Date(caja.dataset.fecha) - Date.now()) / 1000);
    if (resto <= 0) {
      caja.innerHTML = `<p class="cuenta-fin">${esc(caja.dataset.fin)}</p>`;
      return;
    }
    const d = Math.floor(resto / 86400); resto %= 86400;
    const h = Math.floor(resto / 3600);  resto %= 3600;
    const m = Math.floor(resto / 60);    const s = resto % 60;
    caja.innerHTML = `
      <div class="cuenta" role="timer" aria-label="Cuenta atrás">
        <div><b>${d}</b><span>días</span></div>
        <div><b>${dos(h)}</b><span>horas</span></div>
        <div><b>${dos(m)}</b><span>min</span></div>
        <div><b>${dos(s)}</b><span>seg</span></div>
      </div>`;
  });
}

/* --- Fórmula 1 --- */

function proximaF1(compacta = false) {
  const c = estado.mundial.proximaCarrera;
  if (!c) return `<p class="ronda">Temporada ${esc(estado.mundial.temporada)}</p><h2>Temporada terminada</h2>`;

  const trazado = !compacta && c.trazado ? `
    <div class="trazado" aria-hidden="true">
      <svg viewBox="0 0 220 130">
        <path d="${esc(c.trazado)}"/>
        ${c.meta ? `<path class="meta-linea" d="${esc(c.meta)}"/>` : ""}
      </svg>
    </div>` : "";

  const sesiones = compacta ? "" : `<ul class="sesiones">${(c.sesiones || []).map((s) => `
    <li class="${s.nombre === "Carrera" ? "carrera" : ""}">
      <span>${esc(s.nombre)}</span><span>${fmtHora.format(new Date(s.fecha))}</span>
    </li>`).join("")}</ul>`;

  return `
    <p class="ronda">Ronda ${c.ronda} de ${estado.mundial.totalRondas}</p>
    <h2>${esc(c.nombre)}</h2>
    <p class="lugar">${esc(c.circuito)}, ${esc(c.pais)}</p>
    ${trazado}
    ${cuentaHtml(c.fecha, "Carrera en marcha o terminada")}
    ${sesiones}`;
}

function filasF1(limite) {
  const filas = estado.tabla === "pilotos" ? estado.mundial.pilotos : estado.mundial.constructores;
  const seguidos = SECCIONES.filter((s) => s.codigo);
  return (filas || []).slice(0, limite).map((f) => {
    const seguido = seguidos.find((s) => s.codigo === f.codigo);
    const nombre = estado.tabla === "pilotos"
      ? `${esc(f.nombre)}<small>${esc(f.equipo)}</small>`
      : esc(f.nombre);
    return `
      <li class="${seguido ? "seguido" : ""}" style="--color:${esc(f.color)};${seguido ? `--marca:${seguido.color}` : ""}">
        <span class="pos">${f.pos}</span>
        <span class="franja"></span>
        <span class="nombre">${nombre}</span>
        <span class="puntos">${f.puntos}</span>
      </li>`;
  }).join("");
}

function calendarioF1() {
  const proxima = estado.mundial.proximaCarrera?.ronda ?? Infinity;
  return `<ol class="calendario">${(estado.mundial.calendario || []).map((c) => {
    const clase = c.ronda < proxima ? "pasada" : c.ronda === proxima ? "siguiente" : "";
    return `
      <li class="${clase}">
        <span class="r">${c.ronda}</span>
        <span>${esc(c.nombre)}${c.ganador ? `<span class="ganador">Ganó ${esc(c.ganador)}</span>` : ""}</span>
        <span class="f">${fmtDia.format(new Date(c.fecha))}</span>
      </li>`;
  }).join("")}</ol>`;
}

/* --- Otros campeonatos (F2, F3…) --- */

function proximaOtro(c) {
  const r = c.calendario.find((x) => x.ronda === c.proximaRonda);
  if (!r) {
    const primero = c.pilotos[0];
    return `
      <p class="ronda">Temporada ${esc(c.temporada)} terminada</p>
      <h2>${primero ? `Campeón: ${esc(primero.nombre)}` : esc(c.nombre)}</h2>
      ${primero ? `<p class="lugar">${primero.puntos} puntos</p>` : ""}`;
  }
  return `
    <p class="ronda">Ronda ${r.ronda} de ${c.totalRondas}</p>
    <h2>${esc(r.lugar)}</h2>
    <p class="lugar">${esc(r.pais)} · ${rangoFechas(r.inicio, r.fin)}</p>
    ${cuentaHtml(`${r.inicio}T00:00:00`, "Fin de semana en marcha")}`;
}

// Equipos solo si el campeonato los tiene y está elegida esa pestaña
const verEquipos = (c) => estado.tabla === "constructores" && c.equipos?.length > 0;

function filasOtro(c, limite, segunPestana = false) {
  // Equipos: se muestran todos (son pocos); pilotos: los primeros
  const filas = segunPestana && verEquipos(c) ? c.equipos : c.pilotos.slice(0, limite);
  return filas.map((p) => `
    <li style="--color:${colorCampeonato(c.id)}">
      <span class="pos">${p.pos}</span>
      <span class="franja"></span>
      <span class="nombre">${esc(p.nombre)}</span>
      <span class="puntos">${p.puntos}</span>
    </li>`).join("");
}

function calendarioOtro(c) {
  const proxima = c.proximaRonda ?? Infinity;
  return `<ol class="calendario">${c.calendario.map((r) => {
    const clase = r.ronda < proxima ? "pasada" : r.ronda === proxima ? "siguiente" : "";
    return `
      <li class="${clase}">
        <span class="r">${r.ronda}</span>
        <span>${esc(r.lugar)}<span class="ganador">${esc(r.pais)}</span></span>
        <span class="f">${rangoFechas(r.inicio, r.fin)}</span>
      </li>`;
  }).join("")}</ol>`;
}

/* --- Bloque completo de un campeonato: próxima ronda, clasificación y calendario --- */

function bloqueCompleto(id) {
  if (id === "F1") {
    return `
      <section class="bloque proxima">${proximaF1()}</section>
      <section class="bloque">
        <div class="bloque-cab">
          <h2>Mundial</h2>
          <div class="pestanas" role="tablist">
            <button role="tab" aria-selected="${estado.tabla === "pilotos"}" data-tabla="pilotos">Pilotos</button>
            <button role="tab" aria-selected="${estado.tabla === "constructores"}" data-tabla="constructores">Equipos</button>
          </div>
        </div>
        <ol class="torre">${filasF1()}</ol>
      </section>
      <section class="bloque">
        <div class="bloque-cab"><h2>Calendario</h2></div>
        ${calendarioF1()}
      </section>`;
  }
  const c = otroCampeonato(id);
  if (!c) {
    // Campeonato sin datos todavía (la Fórmula E, por ahora)
    return `
      <section class="bloque proxima" style="--acento:${colorCampeonato(id)}">
        <p class="campeonato-nombre">${esc(NOMBRES_CAMPEONATO[id] || id)}</p>
        <h2>Datos próximamente</h2>
        <p class="lugar">Pronto podrás ver aquí la clasificación y el calendario de este campeonato.</p>
      </section>`;
  }
  return `
    <section class="bloque proxima" style="--acento:${colorCampeonato(id)}">
      <p class="campeonato-nombre">${esc(c.nombre)}</p>
      ${proximaOtro(c)}
    </section>
    <section class="bloque">
      <div class="bloque-cab">
        <h2>Clasificación</h2>
        ${c.equipos?.length ? `
        <div class="pestanas" role="tablist">
          <button role="tab" aria-selected="${estado.tabla === "pilotos"}" data-tabla="pilotos">Pilotos</button>
          <button role="tab" aria-selected="${estado.tabla === "constructores"}" data-tabla="constructores">Equipos</button>
        </div>` : ""}
      </div>
      <ol class="torre">${filasOtro(c, 10, true)}</ol>
      ${verEquipos(c) ? "" : `<p class="nota-torre">10 primeros</p>`}
    </section>
    <section class="bloque">
      <div class="bloque-cab"><h2>Calendario</h2></div>
      ${calendarioOtro(c)}
    </section>`;
}

/* --- Columna lateral (ordenador) y vista «Mundial» (móvil) --- */

function pintarLateral() {
  if (!estado.mundial) return;
  let html = "";
  if (estado.filtro === "mundial") {
    // Pestañas por campeonato, todas desplegadas; la F1 por defecto
    const ids = campeonatosDisponibles();
    if (!ids.includes(estado.pestanaMundial)) estado.pestanaMundial = "F1";
    html += `<div class="pestanas-mundial" role="tablist">${ids.map((id) => `
      <button role="tab" data-campeonato="${id}" aria-selected="${id === estado.pestanaMundial}"
              style="--color:${colorCampeonato(id)}">${id}</button>`).join("")}</div>`;
    html += bloqueCompleto(estado.pestanaMundial);
  } else {
    html += bloqueCompleto(campeonatoDeSeccion(estado.filtro));
  }
  if (estado.mundial.ejemplo) html += `<p class="aviso-datos">Datos del Mundial de ejemplo</p>`;
  $("#lateral").innerHTML = html;
  actualizarCuenta();
}

/* --- Tarjeta compacta del campeonato (solo móvil): secciones F1, F2, F3 y FE --- */

function tarjetaCampeonato() {
  if (!estado.mundial) return "";
  const id = estado.filtro;
  if (!["F1", "F2", "F3", "FE"].includes(id)) return "";
  if (id !== "F1" && !otroCampeonato(id)) return "";

  const abierta = estado.tarjetaAbierta === id;
  const titulo = id === "F1" ? "Mundial de Fórmula 1" : `Campeonato de ${NOMBRES_CAMPEONATO[id]}`;
  const plegada = id === "F1"
    ? `${proximaF1(true)}<ol class="torre">${filasF1(3)}</ol>`
    : `${proximaOtro(otroCampeonato(id))}<ol class="torre">${filasOtro(otroCampeonato(id), 3)}</ol>`;

  return `
    <section class="tarjeta-campeonato" style="--color:${colorCampeonato(id)}">
      <p class="tc-titulo">${titulo}</p>
      ${abierta ? `<div class="tc-completo">${bloqueCompleto(id)}</div>` : `<div class="tc-plegada">${plegada}</div>`}
      <button class="tc-boton" data-desplegar="${id}" aria-expanded="${abierta}">
        ${abierta ? "Ocultar" : "Ver clasificación y calendario"}
      </button>
    </section>`;
}

/* ---------- Pie: año y correo ---------- */

function prepararPie() {
  $("#anio").textContent = new Date().getFullYear();
  const enlace = $("#correo");
  if (enlace) {
    const correo = `${enlace.dataset.usuario}@${enlace.dataset.dominio}`;
    enlace.href = `mailto:${correo}`;
    enlace.textContent = correo;
  }
}

/* ---------- Formulario de contacto ---------- */

function prepararContacto() {
  const ventana = $("#contacto");
  const form = $("#form-contacto");
  const estadoMsg = $("#contacto-estado");
  if (!ventana || !form) return;

  // Al abrir, siempre se muestra el formulario vacío (aunque antes se enviara un mensaje)
  document.querySelectorAll("[data-abrir-contacto]").forEach((b) =>
    b.addEventListener("click", () => {
      form.classList.remove("enviado");
      estadoMsg.textContent = "";
      ventana.showModal();
    }));
  $("#cerrar-contacto").addEventListener("click", () => ventana.close());
  $("#exito-cerrar").addEventListener("click", () => ventana.close());

  form.addEventListener("submit", async (e) => {
    e.preventDefault();
    const clave = form.dataset.clave;
    if (!clave || clave.startsWith("PEGA-AQUI")) {
      estadoMsg.className = "contacto-estado mal";
      estadoMsg.textContent = "Falta configurar la clave de Web3Forms en index.html.";
      return;
    }
    const boton = form.querySelector(".enviar");
    const datos = Object.fromEntries(new FormData(form));
    datos.access_key = clave;
    datos.subject = "Nuevo mensaje desde Pit Wall";
    datos.from_name = "Pit Wall";

    boton.disabled = true;
    boton.textContent = "Enviando…";
    estadoMsg.className = "contacto-estado";
    estadoMsg.textContent = "";
    try {
      const r = await fetch("https://api.web3forms.com/submit", {
        method: "POST",
        headers: { "Content-Type": "application/json", Accept: "application/json" },
        body: JSON.stringify(datos)
      });
      const res = await r.json();
      if (!res.success) throw new Error(res.message);
      form.reset();
      form.classList.add("enviado");   // oculta los campos y muestra el mensaje de enviado
    } catch (err) {
      console.error(err);
      estadoMsg.className = "contacto-estado mal";
      estadoMsg.textContent = "No se ha podido enviar. Comprueba la conexión e inténtalo de nuevo.";
    } finally {
      boton.disabled = false;
      boton.textContent = "Enviar mensaje";
    }
  });
}

/* ---------- App instalable (PWA) ---------- */

function prepararApp() {
  if ("serviceWorker" in navigator) {
    navigator.serviceWorker.register("sw.js").catch((err) => console.warn("Service worker:", err));
  }

  // En iPhone/iPad, Safari no avisa de que se puede instalar: se muestra un aviso una sola vez
  const esIOS = /iphone|ipad|ipod/i.test(navigator.userAgent) ||
                (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1);
  const instalada = window.matchMedia("(display-mode: standalone)").matches || navigator.standalone;
  let yaVisto = false;
  try { yaVisto = localStorage.getItem("aviso-instalar") === "visto"; } catch {}

  const aviso = $("#aviso-instalar");
  if (aviso && esIOS && !instalada && !yaVisto) {
    aviso.hidden = false;
    $("#cerrar-aviso").addEventListener("click", () => {
      aviso.hidden = true;
      try { localStorage.setItem("aviso-instalar", "visto"); } catch {}
    });
  }
}

/* ---------- Arranque ---------- */

async function cargar(ruta) {
  const r = await fetch(`${ruta}?v=${Date.now()}`);
  if (!r.ok) throw new Error(`${ruta}: ${r.status}`);
  return r.json();
}

async function iniciar() {
  prepararPie();
  prepararContacto();
  prepararApp();
  let idiomaGuardado = "es";
  try { idiomaGuardado = localStorage.getItem("idioma") || "es"; } catch {}
  cambiarIdioma(idiomaGuardado, false);
  const desdeEnlace = location.hash.slice(1);
  if (desdeEnlace && (seccion(desdeEnlace) || desdeEnlace === "mundial")) estado.filtro = desdeEnlace;
  pintarFiltros();

  await cargarDatos();
  setInterval(actualizarCuenta, 1000);
  setInterval(actualizarHoras, 60000);   // refresca los «hace X min» sin volver a pintar las noticias

  // Si la web (o la app) se queda abierta, vuelve a pedir los datos cada 15 minutos
  // y también al volver a ella después de un rato en segundo plano
  setInterval(cargarDatos, 15 * 60 * 1000);
  document.addEventListener("visibilitychange", () => {
    if (document.visibilityState === "visible" && Date.now() - estado.ultimaCarga > 5 * 60 * 1000) cargarDatos();
  });
}

// Cambia solo los textos «hace X minutos», sin tocar las fotos
function actualizarHoras() {
  document.querySelectorAll("time[datetime]").forEach((t) => {
    t.textContent = haceCuanto(t.getAttribute("datetime"));
  });
}

async function cargarDatos() {
  try {
    const [datos, mundial, otros] = await Promise.all([
      cargar("data/noticias.json"),
      cargar("data/mundial.json"),
      cargar("data/campeonatos.json").catch(() => null)   // opcional: si no existe, solo se muestra la F1
    ]);

    // Si no ha cambiado nada desde la última carga, no se vuelve a pintar (así las fotos no parpadean)
    const huella = `${datos.actualizado}|${mundial.actualizado ?? ""}|${otros?.actualizado ?? ""}`;
    if (huella === estado.huella) { estado.ultimaCarga = Date.now(); return; }
    estado.huella = huella;
    estado.noticias = datos.noticias.sort((a, b) => new Date(b.fecha) - new Date(a.fecha));
    estado.mundial = mundial;
    estado.campeonatos = otros?.campeonatos ?? [];
    estado.ultimaCarga = Date.now();

    $("#actualizado").textContent = datos.ejemplo
      ? "Noticias de ejemplo"
      : `Actualizado el ${fmtAct.format(new Date(datos.actualizado))}`;

    pintarPrincipal();
    pintarLateral();
  } catch (err) {
    console.error(err);
    // Si ya había datos (p. ej. sin conexión un momento), se dejan los que había
    if (!estado.noticias.length) {
      $("#principal").innerHTML = `<p class="error">No se han podido cargar los datos. Abre la página con Live Server (no con doble clic en el archivo) y comprueba que existe la carpeta data.</p>`;
    }
  }
}

iniciar();
