/* =========================================================
   PIT WALL · Service worker
   Guarda una copia de la web en el móvil para que se abra
   al instante y funcione sin conexión.
   - Archivos de la web y datos: primero Internet; si no hay
     conexión, la copia guardada.
   - Fotos de las noticias: se guardan las últimas que se han
     visto, para poder mostrarlas sin conexión.
   Si cambias la lista de archivos, sube el número de VERSION.
   ========================================================= */

const VERSION = "pitwall-v14";
const CACHE_WEB = `${VERSION}-web`;
const CACHE_FOTOS = `${VERSION}-fotos`;
const MAX_FOTOS = 80;

const ARCHIVOS_WEB = [
  "./",
  "index.html",
  "css/estilos.css",
  "js/app.js",
  "data/noticias.json",
  "data/mundial.json",
  "data/campeonatos.json",
  "img/sin-foto.svg",
  "img/favicon-v2.svg",
  "img/iconos/v2/icono-192.png",
  "manifest.webmanifest"
];

// Instalación: se guarda la web completa
self.addEventListener("install", (evento) => {
  evento.waitUntil(caches.open(CACHE_WEB).then((cache) => cache.addAll(ARCHIVOS_WEB)));
  self.skipWaiting();
});

// Activación: se borran las copias de versiones anteriores
self.addEventListener("activate", (evento) => {
  evento.waitUntil(
    caches.keys().then((claves) =>
      Promise.all(claves.filter((c) => !c.startsWith(VERSION)).map((c) => caches.delete(c))))
  );
  self.clients.claim();
});

self.addEventListener("fetch", (evento) => {
  const peticion = evento.request;
  if (peticion.method !== "GET") return;
  const url = new URL(peticion.url);

  if (url.origin === self.location.origin) {
    evento.respondWith(primeroInternet(peticion));
  } else if (peticion.destination === "image") {
    evento.respondWith(fotoNoticia(peticion));
  }
});

// Archivos propios: Internet primero y, sin conexión, la copia guardada
async function primeroInternet(peticion) {
  const cache = await caches.open(CACHE_WEB);
  try {
    const respuesta = await fetch(peticion);
    if (respuesta.ok) cache.put(peticion, respuesta.clone());
    return respuesta;
  } catch {
    const guardada = await cache.match(peticion, { ignoreSearch: true });
    return guardada || (await cache.match("index.html"));
  }
}

// Fotos de los medios: la copia guardada si existe; si no, se descarga y se guarda
async function fotoNoticia(peticion) {
  const cache = await caches.open(CACHE_FOTOS);
  const guardada = await cache.match(peticion);
  if (guardada) return guardada;
  try {
    const respuesta = await fetch(peticion);
    cache.put(peticion, respuesta.clone()).then(() => recortar(cache));
    return respuesta;
  } catch {
    return caches.match("img/sin-foto.svg");
  }
}

async function recortar(cache) {
  const claves = await cache.keys();
  for (const clave of claves.slice(0, Math.max(0, claves.length - MAX_FOTOS))) await cache.delete(clave);
}
