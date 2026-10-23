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
const estado = { noticias: [], mundial: null, filtro: "todo", tabla: "pilotos", idioma: "es", ultimaCarga: 0, huella: "" };

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
  let html = s.codigo ? fichaPiloto(s) : "";
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
  pintarPrincipal();
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
    document.querySelectorAll("[data-tabla]").forEach((b) =>
      b.setAttribute("aria-selected", String(b === pestana)));
    pintarTorre();
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

/* ---------- Mundial: próxima carrera, clasificación y calendario ---------- */

function pintarProxima() {
  const c = estado.mundial.proximaCarrera;
  if (!c) { $("#proxima").innerHTML = ""; return; }

  const trazado = c.trazado ? `
    <div class="trazado" aria-hidden="true">
      <svg viewBox="0 0 220 130">
        <path d="${esc(c.trazado)}"/>
        ${c.meta ? `<path class="meta-linea" d="${esc(c.meta)}"/>` : ""}
      </svg>
    </div>` : "";

  const sesiones = (c.sesiones || []).map((s) => `
    <li class="${s.nombre === "Carrera" ? "carrera" : ""}">
      <span>${esc(s.nombre)}</span><span>${fmtHora.format(new Date(s.fecha))}</span>
    </li>`).join("");

  $("#proxima").innerHTML = `
    <p class="ronda">Ronda ${c.ronda} de ${estado.mundial.totalRondas}</p>
    <h2>${esc(c.nombre)}</h2>
    <p class="lugar">${esc(c.circuito)}, ${esc(c.pais)}</p>
    ${trazado}
    <div id="cuenta"></div>
    <ul class="sesiones">${sesiones}</ul>`;

  actualizarCuenta();
}

function actualizarCuenta() {
  const caja = $("#cuenta");
  const c = estado.mundial?.proximaCarrera;
  if (!caja || !c) return;
  let resto = Math.floor((new Date(c.fecha) - Date.now()) / 1000);
  if (resto <= 0) {
    caja.innerHTML = `<p class="cuenta-fin">Carrera en marcha o terminada</p>`;
    return;
  }
  const d = Math.floor(resto / 86400); resto %= 86400;
  const h = Math.floor(resto / 3600);  resto %= 3600;
  const m = Math.floor(resto / 60);    const s = resto % 60;
  const dos = (n) => String(n).padStart(2, "0");
  caja.innerHTML = `
    <div class="cuenta" role="timer" aria-label="Tiempo hasta la carrera">
      <div><b>${d}</b><span>días</span></div>
      <div><b>${dos(h)}</b><span>horas</span></div>
      <div><b>${dos(m)}</b><span>min</span></div>
      <div><b>${dos(s)}</b><span>seg</span></div>
    </div>`;
}

function pintarTorre() {
  const filas = estado.tabla === "pilotos" ? estado.mundial.pilotos : estado.mundial.constructores;
  const seguidos = SECCIONES.filter((s) => s.codigo);
  $("#torre").innerHTML = (filas || []).map((f) => {
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

function pintarCalendario() {
  const proxima = estado.mundial.proximaCarrera?.ronda;
  $("#calendario").innerHTML = (estado.mundial.calendario || []).map((c) => {
    const clase = c.ronda < proxima ? "pasada" : c.ronda === proxima ? "siguiente" : "";
    return `
      <li class="${clase}">
        <span class="r">${c.ronda}</span>
        <span>${esc(c.nombre)}${c.ganador ? `<span class="ganador">Ganó ${esc(c.ganador)}</span>` : ""}</span>
        <span class="f">${fmtDia.format(new Date(c.fecha))}</span>
      </li>`;
  }).join("");
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
    const [datos, mundial] = await Promise.all([cargar("data/noticias.json"), cargar("data/mundial.json")]);

    // Si no ha cambiado nada desde la última carga, no se vuelve a pintar (así las fotos no parpadean)
    const huella = `${datos.actualizado}|${mundial.actualizado ?? ""}`;
    if (huella === estado.huella) { estado.ultimaCarga = Date.now(); return; }
    estado.huella = huella;
    estado.noticias = datos.noticias.sort((a, b) => new Date(b.fecha) - new Date(a.fecha));
    estado.mundial = mundial;
    estado.ultimaCarga = Date.now();

    $("#actualizado").textContent = datos.ejemplo
      ? "Noticias de ejemplo"
      : `Actualizado el ${fmtAct.format(new Date(datos.actualizado))}`;
    $("#aviso-datos").textContent = mundial.ejemplo ? "Datos del Mundial de ejemplo" : "";

    pintarPrincipal();
    pintarProxima();
    pintarTorre();
    pintarCalendario();
  } catch (err) {
    console.error(err);
    // Si ya había datos (p. ej. sin conexión un momento), se dejan los que había
    if (!estado.noticias.length) {
      $("#principal").innerHTML = `<p class="error">No se han podido cargar los datos. Abre la página con Live Server (no con doble clic en el archivo) y comprueba que existe la carpeta data.</p>`;
    }
  }
}

iniciar();
