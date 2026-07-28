# TaskbarTunes

Widget de música para la barra de tareas de Windows 11: muestra lo que estás
escuchando (Spotify, YouTube o cualquier web/app con medios), con carátula,
controles de reproducción y un visualizador de audio animado. Todo personalizable.

![TaskbarTunes en la barra de tareas de Windows 11](docs/screenshot.png)

*Read this in [English](README.md).*

Ventana overlay sin bordes anclada sobre la barra de tareas + API global de
medios de Windows (GSMTC) + captura loopback WASAPI con FFT.

## Ejecutable

El binario listo para usar queda en:

```
bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\TaskbarTunes.exe
```

Requiere el runtime de .NET 8 Desktop (ya instalado con el SDK). Puedes copiar
el `.exe` a donde quieras.

## Uso

- **Un clic** → abre el panel expandido: carátula grande, título completo,
  barra de tiempo con seek y tiempos (1:23 / 4:05). Se cierra al hacer clic fuera o con Esc.
- **Clic en la barrita de progreso** → salta a ese punto de la canción.
- **Arrastrar** → recoloca el widget (por toda la barra, o por toda la pantalla en modo libre).
- **Doble clic** → acción configurable (play/pausa por defecto).
- **Clic derecho** → fuente de música, historial de canciones (últimas 50, clic
  para copiar), modo overlay libre, Ajustes, salir.
- **Pasar el ratón** → aparecen los controles ⏮ ⏯ ⏭.
- **Icono de la bandeja** (nota musical verde) → Ajustes, "Iniciar con Windows", Salir.
- Los ajustes se aplican en vivo y se guardan en `%APPDATA%\TaskbarTunes\settings.json`;
  el historial en `history.json` (todo local).

## Personalización (ventana de Ajustes, en pestañas)

- **Apariencia**: ancho, radio de esquinas, posición (junto al reloj / centro /
  izquierda / personalizada / overlay libre), tipografía (fuente y tamaño) y
  colores de fondo y texto con **selector visual HSV** (con transparencia) que
  aplica en vivo.
- **Posición**: arrastra el widget con el ratón por el 100% de la barra, o
  activa el **modo overlay libre** (clic derecho → «Modo overlay libre») para
  flotarlo en cualquier punto de la pantalla, por encima de juegos en ventana
  o sin bordes.
- **Visualizador**: 6 estilos (barras, barras espejadas, onda, onda rellena,
  puntos, LEDs retro), número de barras, separación, opacidad, degradado con
  dirección, **color adaptativo desde la carátula** 🎨 y **modo «beat»** (solo
  graves) 🥁.
- **Contenido**: carátula, artista, controles, **barra de progreso de la
  canción**, icono de la fuente, limpieza de títulos de YouTube y ocultar el
  widget cuando no suena nada.
- **Temas**: 5 temas integrados (Spotify, Neón, Monocromo, Ámbar retro,
  Adaptativo) y **presets propios** (guardar/aplicar/eliminar, en
  `%APPDATA%\TaskbarTunes\presets\`).
- **Sistema**: fuente preferida, acción del doble clic (play/pausa, abrir la
  app de música, abrir Ajustes) y autoarranque.
- **Extras**: fondo acrílico nativo de Win11 (desenfoque real), carátula en modo
  vinilo girando 💿 con crossfade al cambiar de canción, panel expandido con seek,
  y soporte multi-monitor (elige pantalla; en modo libre puedes arrastrarlo a
  cualquier monitor).

## Compilar desde el código

```powershell
dotnet build                                   # desarrollo
dotnet publish -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true
```

## Compartir con otras personas

Compilación autocontenida (un solo `.exe`, ~75 MB, no requiere instalar .NET):

```powershell
dotnet publish -c Release -r win-x64 --self-contained true `
  /p:PublishSingleFile=true /p:EnableCompressionInSingleFile=true `
  /p:IncludeNativeLibrariesForSelfExtract=true
```

El zip queda en `dist\` (no se versiona: los binarios van en GitHub Releases).
Requisitos del
receptor: Windows 10 2004 o superior / Windows 11. Al ejecutarlo por primera
vez, SmartScreen mostrará un aviso porque el exe no está firmado digitalmente:
«Más información → Ejecutar de todas formas». Es cosmético — firmar requiere un
certificado de pago.

Sobre la protección del código: el exe no contiene archivos fuente y va
comprimido, pero como toda app .NET su IL puede descompilarse con herramientas
especializadas (ILSpy). Para dificultarlo más existe la ofuscación (Obfuscar,
.NET Reactor), con el riesgo de romper el enlace XAML↔código en WPF.

## Cómo funciona

| Pieza | Técnica |
|---|---|
| Anclaje a la barra | Ventana WPF sin bordes posicionada sobre `Shell_TrayWnd` vía Win32; timer de 500 ms re-afirma topmost y sigue el auto-ocultado |
| Info de la música | `Windows.Media.Control` (GSMTC): título, artista, carátula y play/pausa de Spotify y navegadores, sin APIs web ni logins |
| Visualizador | **Loopback por proceso** (Windows 10 2004+): captura solo el audio de la app de música y sus procesos hijos — juegos y otras apps no lo mueven. FFT 1024 con ventana Hann → 64 bandas logarítmicas con auto-ganancia → render a ~33 fps. Configurable a "todo el sistema" en Ajustes → Visualizador |

## Limitaciones conocidas

- La carátula desde YouTube depende de la miniatura que entregue el navegador.
- Con Firefox la fuente puede clasificarse como «Otros» (su identificador de app
  no contiene "firefox"); la música se muestra igual, pero sin limpieza de títulos
  y el visualizador cae al modo "todo el sistema".
- Si la barra de tareas está en modo auto-ocultar, el widget se oculta y aparece con ella.
- El modo overlay libre se ve sobre juegos en ventana o pantalla completa **sin
  bordes**; sobre pantalla completa *exclusiva* Windows no permite dibujar encima.
- La barra de progreso depende de que la app reporte la duración (Spotify sí;
  en navegador depende del sitio).

## Licencia

[GPL-3.0](LICENSE) — puedes usarlo, estudiarlo, modificarlo y compartirlo, y
cualquier trabajo derivado debe seguir siendo abierto bajo la misma licencia.
