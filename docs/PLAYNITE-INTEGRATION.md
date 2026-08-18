# Integración con Playnite

## 1. Superficie oficial confirmada

CSM será un `GenericPlugin`. La API pública actual permite:

- ciclo de aplicación: `OnApplicationStarted`, `OnApplicationStopped`, `Dispose`;
- ciclo de juego: `OnGameStarting`, `OnGameStarted`, `OnGameStopped`, `OnGameStartupCancelled`;
- controladores: `OnControllerConnected`, `OnControllerDisconnected`, `OnControllerButtonStateChanged`, `OnDesktopControllerButtonStateChanged` y `GetConnectedControllers`;
- settings WPF mediante `HasSettings`, `GetSettings` y `GetSettingsView`;
- integración oficial en themes mediante `AddCustomElementSupport`, `GetGameViewControl`, `AddSettingsSupport` y `AddConvertersSupport`;
- ruta persistente mediante `GetPluginUserDataPath`.

No se asumirá acceso a APIs internas de Playnite ni se referenciarán ensamblados no SDK.

## 2. Versión y compatibilidad

La documentación pública actual de plugins sigue indicando .NET Framework 4.6.2. Antes de crear el `.csproj` se realizará un spike con el paquete oficial `PlayniteSDK` y la versión estable instalada. La compatibilidad mínima se expresará en `extension.yaml` y CI compilará contra la versión mínima y la actual.

Playnite advierte que su SDK no es completamente thread-safe. Ningún callback nativo accederá directamente a `PlayniteApi`, WPF o colecciones observables. Las actualizaciones visuales se despacharán con `PlayniteApi.MainView.UIDispatcher`.

## 3. Ciclo de vida propuesto

| Evento | Acción CSM | Garantía de cleanup |
|---|---|---|
| Constructor | Registrar custom elements/settings/converters; construir servicios sin arrancar hardware | Sin threads |
| `OnApplicationStarted` | Cargar settings, validar runtime, iniciar monitor básico | Token de aplicación |
| `OnGameStarting` | Resolver policy/override; preparar contexto, sin pausa/overlay aún | Cancelable por startup cancelled |
| `OnGameStarted` | Crear sesión con `Game.Id` y `StartedProcessId`; armar active tracker | Token de sesión único |
| `OnGameStartupCancelled` | Descartar contexto preparado | Idempotente |
| `OnGameStopped` | Cancelar protección, ocultar overlay, liberar receipt y tracker | Continúa monitor básico |
| `OnApplicationStopped` | Parada ordenada global | Timeout acotado |
| `Dispose` | Segunda barrera idempotente de cleanup | Puede llamarse tras parada parcial |

`StartedProcessId` puede no ser válido para todos los lanzamientos, según la propia API. El `GameTargetResolver` lo tratará como pista inicial y verificará existencia, ventana y relación de procesos. Sin target fiable, `SendKey` se omite y el overlay aún puede mostrarse.

Desde 0.3.0, la primera estrategia `SendEscape` está implementada como opción desactivada por defecto. En cada incidencia agregada sólo se intenta una vez. Se obtiene el HWND foreground, su PID y el snapshot de padres con Toolhelp; el PID debe coincidir con `StartedProcessId` o descender de él. Antes de `SendInput` se vuelve a comprobar HWND y PID. El resultado se conserva como `PauseReceipt` para diagnóstico y presentación, pero nunca se usa para reanudar automáticamente.

En 0.3.1 la estrategia acepta una tecla simple validada. El ajuste global define la tecla y el override por `Game.Id` almacena `overlay only`, `Escape` o una copia de la tecla configurada. Los modificadores y secuencias quedan rechazados. Un `PauseAttemptGate` agregado a la incidencia garantiza que dos desconexiones cooperativas no generen dos pulsaciones.

En 0.3.2 el override diferencia explícitamente la herencia de protección y la de pausa. Los valores antiguos sin marcadores se interpretan como overrides completos para no cambiar silenciosamente el comportamiento ya guardado. `GetGameMenuItems` crea dos rutas anidadas y antepone `✓` a la selección común; el SDK no expone una propiedad `IsChecked` para `GameMenuItem`, por lo que el carácter visible es la solución compatible con temas.

En 0.4.0 la protección se reduce a `Automatic`, `LocalMultiplayer` y `Disabled`. La biblioteca de Playnite puede contener características de cooperativo u online, pero no garantiza su presencia, procedencia ni el modo concreto elegido en la ejecución actual. CSM no clasifica automáticamente el juego: usa Automatic por defecto y conserva LocalMultiplayer como elección explícita por `Game.Id`. El multijugador exclusivamente online usa Automatic porque los jugadores remotos no aparecen como dispositivos locales.

En 0.4.1, el relevo automático espera a que el mando alternativo permanezca neutral durante 200 ms. Así una pulsación o un stick mantenido no cierra el overlay mientras la misma entrada todavía podría alcanzar al juego. El sondeo se acelera a 100 ms sólo durante incidencias y vuelve a 250 ms al resolverlas.

En 0.4.2, Guide/PS/Home se trata como botón del sistema y no activa una sesión. El umbral de stick baja a 8.000 unidades y el reposo de relevo a 100 ms para que un movimiento normal cierre el aviso prácticamente al instante.

En 0.4.3, el sondeo de mandos se realiza cada 50 ms durante toda la sesión de juego, no sólo después de detectar una desconexión. Un movimiento breve deja así evidencia antes de volver a reposo. Sin juego activo, el intervalo vuelve a 250 ms.

## 4. Integración de eventos de controlador

En la implementación 1.0.6, `GetConnectedControllers()`, `OnControllerConnected` y `OnControllerDisconnected` son la autoridad del ciclo de vida. La enumeración establece el inventario inicial; los callbacks aplican cambios inmediatos. Dos inventarios consecutivos ausentes sirven únicamente para recuperar una desconexión cuyo callback se hubiera perdido, y no prevalecen mientras una capacidad asociada siga observando el dispositivo.

`GamepadController.Path` se normaliza para correlacionar el registro con XInput o SDL. `InstanceId` queda limitado al proveedor y a la ejecución: sólo se compara con SDL para controladores no XInput, nunca con un slot XInput por coincidencia numérica. XInput, SDL y Windows PnP enriquecen la fila autoritativa con capacidades, pero no modifican su presencia. Si el inventario del SDK no está disponible o no aporta ninguna fila utilizable, los proveedores funcionan como fallback degradado y esa situación queda reflejada en diagnóstico.

## 5. Settings

### Modelo

```text
ControllerSessionManagerSettings : ISettings, INotifyPropertyChanged
  SchemaVersion
  General
  DisconnectProtection
  Pause
  Overlay
  Battery
  Controllers
  Diagnostics
  GameOverrides : Dictionary<Guid, GameOverride>
```

La vista de edición trabaja sobre una copia. `BeginEdit`, `CancelEdit`, `EndEdit` y `VerifySettings` siguen el patrón oficial; ningún cambio agresivo se aplica antes de confirmar.

### Overrides por juego

Clave: `Playnite.SDK.Models.Game.Id`. Motivos:

- es el identificador de la entrada en la base de datos;
- el nombre puede cambiar;
- `Game.GameId` es un identificador del proveedor y no es globalmente único.

La UI ofrece los submenús `Protección de sesión` y `Pausa automática` desde `GetGameMenuItems`. La configuración sigue siendo propiedad del plugin y no muta tags/campos del juego. Los overrides huérfanos se conservan por seguridad y podrán limpiarse manualmente.

## 6. Proceso y ventana del juego

`OnGameStartedEventArgs` incluye `Game`, `SourceAction`, `SelectedRomFile` y `StartedProcessId`. Este PID no siempre es válido. Resolución prudente:

1. aceptar PID sólo si existe y su inicio coincide con la ventana temporal de la sesión;
2. obtener ventanas top-level visibles del PID;
3. si es launcher, observar descendientes creados después del inicio sin asumir que todo el árbol es el juego;
4. puntuar ventana por foreground, área, monitor y nombre/configuración;
5. si hay ambigüedad, `GameTarget.Confidence=Low` y deshabilitar inyección por defecto;
6. permitir override explícito por ejecutable/ventana en una versión posterior.

No se utilizará la mera detección de red o tags para clasificar un juego como online. La seguridad se controla por allow/deny manual y defaults conservadores.

## 7. Errores y aislamiento

Cada override de Playnite es un límite de excepción: captura, registra y devuelve rápido. Un fallo de provider no impedirá que Playnite arranque o pare. El overlay externo usa IPC con heartbeat y puede reiniciarse una vez; una tormenta de fallos abre circuit breaker y deshabilita overlay para la sesión.

Shutdown máximo propuesto: 2 s para detener callbacks/colas y 1 s para cerrar el overlay. No se bloquea el UI thread esperando indefinidamente.

## 8. Notificaciones en Playnite

Las notificaciones internas de Playnite son apropiadas mientras Playnite está visible, pero no sustituyen el overlay durante el juego. Se usarán para:

- runtime GameInput ausente;
- error persistente de provider;
- theme incompatible;
- acceso a diagnóstico/settings.

Las alertas de batería/desconexión durante juego usan el `NotificationCoordinator`, que elige overlay o notificación Playnite según sesión y política.

## 9. Estructura de solución propuesta

```text
src/
  ControllerSessionManager.Plugin/          # GenericPlugin, settings y composition root
  ControllerSessionManager.Core/            # domain/application, netstandard-compatible si viable
  ControllerSessionManager.Windows/         # P/Invoke y proveedores gestionados
  ControllerSessionManager.GameInput.Native/# wrapper C++/CLI o helper (decisión del spike)
  ControllerSessionManager.Overlay/         # ejecutable WPF independiente
tests/
  ControllerSessionManager.Core.Tests/
  ControllerSessionManager.Provider.Tests/
  ControllerSessionManager.IntegrationTests/
samples/
  OverlayThemes/
docs/
```

Si Playnite obliga a empaquetado mono-assembly, se mantendrán proyectos lógicos pero se publicará un conjunto compatible de DLLs en el directorio del add-on. Las dependencias se auditarán contra las ya cargadas por Playnite.

## 10. Elementos custom: mecanismo real

En el constructor:

```csharp
AddCustomElementSupport(new AddCustomElementSupportArgs
{
    SourceName = "ControllerSessionManager",
    ElementList = ControllerElementCatalog.ApiV1Names
});
```

Playnite invoca `GetGameViewControl(GetGameViewControlArgs args)` cuando inicializa la plantilla. CSM devuelve un nuevo `Control` para el nombre solicitado o `null`. Los controles derivarán de `PluginUserControl`; `GameContext` se usa sólo cuando un elemento necesite el juego actualmente enlazado.

Los themes referencian, por ejemplo:

```xml
<ContentControl x:Name="ControllerSessionManager_BatteryIndicator" />
```

Esto requiere soporte tanto del plugin como del theme; no aparece automáticamente en el theme default.

### Indicador automático de la barra superior de Desktop

El `TopPanelItem` opcional de CSM contiene un `PluginUserControl` y abre los ajustes del plugin al pulsarlo. Como `TopPanelItem` es un control interno de Playnite, el control no referencia ese tipo: al cargarse recorre sus ancestros con `VisualTreeHelper` y selecciona aquel cuyo `GetType().Name` sea exactamente `TopPanelItem`.

El ancho se obtiene de `Width` cuando es válido o de `ActualWidth` como fallback. Con menos de 58 px se considera compacto y siempre se muestra únicamente el icono: usa el color semántico del estado cuando hay batería y el color normal del theme cuando no la hay. En anchos de 58 px o más se muestran icono y batería cuando ambos están disponibles. Se escucha `SizeChanged` para responder a cambios del theme o ventana, y el handler se desconecta en `Unloaded`.

El tooltip usa una sola línea: `Nombre: Batería` cuando existe un valor real, o solamente `Nombre` cuando la batería no está disponible. No se generan porcentajes ni estados ficticios.

## 11. Datos para themes: límite y alternativa soportada

Playnite documenta bindings de themes a **settings del plugin** mediante `PluginSettings`, no un binding arbitrario al ViewModel runtime del plugin. No se publicará como setting un estado de mando que cambia continuamente: semánticamente no es configuración y persistiría ruido.

La alternativa flexible soportada es que cada custom element sea un `PluginUserControl` cuyo contenido pueda estilizarse mediante recursos/DependencyProperties documentados. Niveles:

- simple: `BatteryIndicator`, `ControllerStatus`, `ControllerInfo`;
- modular: `ControllerIcon`, `BatteryText`, `BatteryBar`, `ConnectionIcon`, `ControllerCount`;
- avanzado: `ControllerList`, que expone `ItemTemplate`/estilos mediante resource keys estables dentro del control; el theme controla presentación sin obtener acceso a lógica interna.

Se registrarán converters únicamente para bindings que ya estén en el contexto del theme. `PluginConverter` no crea por sí mismo acceso al estado runtime de CSM.

## 12. Catálogo API v1 propuesto

Nombres estables (el prefijo lo aporta `SourceName`):

| Elemento | Contenido | Multi |
|---|---|---|
| `ControllerStatus` | Estado compacto del primario | No |
| `BatteryIndicator` | Icono + nivel honesto | No |
| `ControllerIcon` | Familia/estado | No |
| `ControllerInfo` | Panel detallado | No |
| `BatteryText` | `%`, nivel cualitativo o `—` | No |
| `BatteryBar` | Barra exacta o segmentos discretos | No |
| `ConnectionIcon` | USB/BT/wireless/unknown | No |
| `ControllerCount` | Número conectado/activo | Sí |
| `PrimaryController` | Control compuesto | No |
| `ActiveController` | Más reciente de sesión | No |
| `ControllerList` | Items de todos/activos según propiedad | Sí |
| `PlayerSlot1`…`PlayerSlot4` | Slot lógico si existe | Sí |

Las propiedades visuales y resource keys quedan en `THEME-INTEGRATION.md`. Añadir elementos es compatible; renombrar o cambiar semántica requiere nueva major de Theme API.

## 13. Fuentes oficiales

### Flujo de overlay implementado

Tras `DisconnectConfirmed`, el plugin inicia bajo demanda `ControllerSessionManager.OverlayHost.exe` y le envía el estado localizado mediante un named pipe restringido al usuario actual y protegido con un token aleatorio por instancia. El host muestra una ventana WPF topmost, no activable y click-through en el monitor de la ventana principal del proceso del juego, con fallback al monitor primario.

La misma incidencia se actualiza si faltan varios mandos. `DisconnectResolved`, `ControllerTakeover`, `OnGameStopped`, desactivar el seguimiento o cerrar Playnite ocultan el overlay. Un heartbeat evita ventanas huérfanas: el host la oculta tras 8 segundos sin comunicación y termina tras 30 segundos o al desaparecer el proceso padre.

### Alcance de protección de la sesión

El modo global es adaptativo. Empieza siguiendo el mando usado más recientemente y promociona la sesión a cooperativo local después de observar participación alternada sostenida de varios mandos. Una sola transición A→B conserva el comportamiento de un jugador; la secuencia A→B→A→B dentro de veinte segundos, con al menos dos muestras significativas por mando, protege a ambos como participantes independientes. El override `Multijugador local` fuerza directamente ese alcance para títulos atípicos.

En modo multijugador, sólo un mando que todavía no pertenecía a la sesión puede sustituir automáticamente a un participante ausente. Tras la sustitución se guarda como suelo el último timestamp de input del mando retirado; una observación antigua o una simple reconexión no puede reactivarlo, pero un input genuinamente posterior sí. El menú contextual permite fijar Automatic o LocalMultiplayer por `Game.Id`.

- [Generic plugins](https://api.playnite.link/docs/tutorials/extensions/genericPlugins.html)
- [Eventos de extensiones](https://api.playnite.link/docs/tutorials/extensions/events.html)
- [OnGameStartedEventArgs](https://api.playnite.link/docs/api/Playnite.SDK.Events.OnGameStartedEventArgs.html)
- [Plugin settings](https://api.playnite.link/docs/tutorials/extensions/pluginSettings.html)
- [Custom UI integration para extensiones](https://api.playnite.link/docs/tutorials/extensions/customUiIntegration.html)
- [Integración de elementos para themes](https://api.playnite.link/docs/tutorials/themes/extensionIntegration.html)
- [GamepadController](https://api.playnite.link/docs/api/Playnite.SDK.Events.GamepadController.html)
- [IPlayniteAPI.GetConnectedControllers](https://api.playnite.link/docs/api/Playnite.SDK.IPlayniteAPI.html)
