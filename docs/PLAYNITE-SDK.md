# Playnite SDK — Documentación unificada

> Fuentes: [api.playnite.link/docs](https://api.playnite.link/docs/tutorials/extensions/intro.html)  
> Generado el 2026-08-17. Cubre la versión de SDK compatible con Playnite 10.

---

## Tabla de contenidos

1. [Introducción a las extensiones](#1-introducción-a-las-extensiones)
2. [Scripting (PowerShell)](#2-scripting-powershell)
3. [Plugins](#3-plugins)
4. [Library Plugins](#4-library-plugins)
5. [Metadata Plugins](#5-metadata-plugins)
6. [Plugin Settings](#6-plugin-settings)
7. [Eventos](#7-eventos)
8. [UI — Interacción con la interfaz de Playnite](#8-ui--interacción-con-la-interfaz-de-playnite)
9. [Web Views](#9-web-views)
10. [Custom UI Integration](#10-custom-ui-integration)
11. [URI Support](#11-uri-support)
12. [Debugging de scripts](#12-debugging-de-scripts)
13. [Localizaciones](#13-localizaciones)
14. [Manifest de extensión](#14-manifest-de-extensión)
15. [Search Support](#15-search-support)
16. [Expanding Variables](#16-expanding-variables)
17. [Menús](#17-menús)
18. [Game Library (API de base de datos)](#18-game-library-api-de-base-de-datos)
19. [Ventanas personalizadas](#19-ventanas-personalizadas)
20. [Logging](#20-logging)
21. [Toolbox utility](#21-toolbox-utility)

---

## 1. Introducción a las extensiones

> https://api.playnite.link/docs/tutorials/extensions/intro.html

Playnite puede extenderse mediante **scripts** (PowerShell) y **plugins** (.NET Framework, cualquier lenguaje compatible: C#, VB.NET, F#…).

> ⚠️ El soporte para extensiones PowerShell se eliminará en Playnite 11. Se recomienda fuertemente usar plugins .NET.

### Capacidades según tipo de extensión

| Funcionalidad | Scripts | Plugins |
|---|---|---|
| Entradas en menú de juego y principal | ✓ | ✓ |
| Reaccionar a eventos de juego | ✓ | ✓ |
| Añadir nuevos elementos UI | — | ✓ |
| Inyectar acciones de juego | — | ✓ |
| Importador de biblioteca | — | ✓ |
| Proveedor de metadatos | — | ✓ |

### Creación de una extensión

1. Crear la carpeta de la extensión dentro de `Extensions`:
   - Portable: carpeta `Extensions` en el directorio de instalación.
   - Instalado: `%AppData%\Playnite\Extensions`.
2. Añadir el archivo de manifest `extension.yaml` (ver [sección 14](#14-manifest-de-extensión)).
3. Implementar el script o plugin.

> ℹ️ Se puede cargar extensiones desde rutas arbitrarias usando `For developers` en los ajustes de Playnite.

### Distribución

Usar `Toolbox.exe pack` para empaquetar la extensión en un archivo `.pext` y distribuirla. El mejor canal de distribución es la base de datos de add-ons de Playnite.

> ⚠️ La instalación/actualización reemplaza el directorio completo de la extensión. Los archivos generados deben guardarse en el directorio de datos de la extensión, no en el directorio de instalación.

---

## 2. Scripting (PowerShell)

> https://api.playnite.link/docs/tutorials/extensions/scripting.html

Los scripts de PowerShell se implementan como módulos `.psm1` o `.psd1`. Se recargan en tiempo de ejecución via `Tools → Reload Scripts`.

> ⚠️ Soporte eliminado en Playnite 11. Migrar a plugins C#.

---

## 3. Plugins

> https://api.playnite.link/docs/tutorials/extensions/plugins.html

### Tipos de plugin

- **Generic Plugin**: equivalente a scripts; permite añadir entradas de menú y reaccionar a eventos.
- **Library Plugin**: importación automática de juegos de fuentes externas.
- **Metadata Plugin**: provisión de metadatos de juegos.

### Crear un plugin desde plantilla

```
Toolbox.exe new GenericPlugin "Mi Plugin" "C:\ruta\destino"
```

Esto genera un proyecto C# con todas las clases base. Abrir el `.sln` resultante en Visual Studio o Rider.

> ℹ️ IDEs recomendados: **Visual Studio** y **Rider**. VS Code no es compatible de forma fiable con .NET Framework 4.6.2.

### Cargar la extensión en Playnite durante desarrollo

Ir a `Ajustes → For developers → External extensions` y añadir el directorio de build output (por ejemplo `bin\Debug\`).

### Depurar un plugin

- `Debug → Attach to process` en Visual Studio y seleccionar el proceso de Playnite.
- O configurar el proyecto para iniciar `Playnite.exe` como programa externo en las opciones de debug.

### Acceder a la API de Playnite

```csharp
// Desde dentro de tu clase plugin:
var games = PlayniteAPI.Database.Games;

// Singleton estático (cuando no se tiene acceso a la propiedad):
var api = Playnite.SDK.API.Instance;
```

> ⚠️ El SDK de Playnite no es completamente thread-safe. Para modificar objetos de UI desde otro hilo, usar `MainView.UIDispatcher`.

### Dependencias externas

Usar las mismas versiones de dependencias que Playnite ya referencia. El sistema de plugins no permite cargar múltiples versiones del mismo ensamblado.

> ⚠️ **No referenciar** ensamblados no-SDK de Playnite (`Playnite.dll`, `Playnite.Common.dll`, etc.). Playnite rechazará cargar ese plugin.

---

## 4. Library Plugins

> https://api.playnite.link/docs/tutorials/extensions/libraryPlugins.html

Heredar de `LibraryPlugin` e implementar los miembros obligatorios:

| Miembro | Descripción |
|---|---|
| `Id` | GUID único del plugin. |
| `Name` | Nombre de la biblioteca. |
| `GetGames` | Devuelve la lista de juegos disponibles. |

Los objetos `Game` devueltos por `GetGames` deben tener correctamente establecidos `GameId`, `PluginId`, `PlayAction` (si instalado) e `InstallDirectory` (si instalado).

### Capacidades opcionales (`LibraryPluginProperties`)

| Capacidad | Descripción |
|---|---|
| `CanShutdownClient` | Permite cerrar el cliente externo tras salir del juego. Requiere implementar `Shutdown`. |
| `HasCustomizedGameImport` | La biblioteca controla el mecanismo de importación. Implementar `ImportGames` en lugar de `GetGames`. |

### Ejemplo

```csharp
public class TestLibrary : LibraryPlugin
{
    public override Guid Id { get; } = Guid.Parse("D625A3B7-1AA4-41CB-9CD7-74448D28E99B");
    public override string Name { get; } = "Test Library";

    public TestLibrary(IPlayniteAPI api) : base(api)
    {
        Properties = new LibraryPluginProperties { CanShutdownClient = true, HasSettings = true };
    }

    public override IEnumerable<GameMetadata> GetGames()
    {
        return new List<GameMetadata>
        {
            new GameMetadata
            {
                Name = "Some App",
                GameId = "some_app_id",
                IsInstalled = true,
                GameActions = new List<GameAction>
                {
                    new GameAction { Type = GameActionType.File, Path = @"c:\some_path\app.exe", IsPlayAction = true }
                }
            }
        };
    }
}
```

---

## 5. Metadata Plugins

> https://api.playnite.link/docs/tutorials/extensions/metadataPlugins.html

Heredar de `MetadataPlugin` e implementar:

| Miembro | Descripción |
|---|---|
| `Id` | GUID único. |
| `Name` | Nombre del proveedor. |
| `SupportedFields` | Lista de campos de metadatos que puede proveer. |
| `GetMetadataProvider` | Devuelve una instancia de `OnDemandMetadataProvider` para una solicitud específica. |

### `OnDemandMetadataProvider`

Sobreescribir los métodos `Get*` según los campos que el plugin puede proveer. Implementar `AvailableFields` para devolver los campos disponibles para la solicitud específica. El objeto es `IDisposable` y se destruye automáticamente al finalizar el procesado de metadatos.

### `MetadataRequestOptions`

- `IsBackgroundDownload`: indica si la descarga es en segundo plano (batch) o manual (desde el diálogo de edición de juego).
- `GameData`: juego que debe ser actualizado.

### Tipos de `MetadataProperty`

| Tipo | Descripción |
|---|---|
| `MetadataNameProperty` | Asigna por nombre (comportamiento Playnite 8). |
| `MetadataIdProperty` | Asigna por ID de objeto existente en la BD. |
| `MetadataSpecProperty` | Asigna por identificador de especificación (plataformas, regiones). |

> ℹ️ En la mayoría de casos solo usar `MetadataNameProperty` o `MetadataSpecProperty`. Playnite gestiona automáticamente la creación y deduplicación.

---

## 6. Plugin Settings

> https://api.playnite.link/docs/tutorials/extensions/pluginSettings.html

### Requisitos

- Establecer `HasSettings = true` en las propiedades del plugin.
- Sobreescribir `GetSettings` (devuelve `ISettings`) y `GetSettingsView` (devuelve `UserControl`).

### Clase de settings

```csharp
public class TestPluginSettings : ObservableObject, ISettings
{
    private TestPlugin plugin;
    private string option1 = string.Empty;
    private bool option2 = false;

    public string Option1 { get => option1; set => SetValue(ref option1, value); }
    public bool Option2 { get => option2; set => SetValue(ref option2, value); }

    [DontSerialize]
    public bool TransientOption { get; set; }

    public TestPluginSettings() { }

    public TestPluginSettings(TestPlugin plugin)
    {
        this.plugin = plugin;
        var saved = plugin.LoadPluginSettings<TestPluginSettings>();
        if (saved != null)
        {
            Option1 = saved.Option1;
            Option2 = saved.Option2;
        }
    }

    public void BeginEdit() { /* Se llama al abrir la vista de settings */ }
    public void CancelEdit() { /* Revertir cambios */ }
    public void EndEdit() { plugin.SavePluginSettings(this); }
    public bool VerifySettings(out List<string> errors) { errors = new List<string>(); return true; }
}
```

### Vista XAML de settings

```xml
<UserControl x:Class="TestPlugin.TestPluginSettingsView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <StackPanel>
        <TextBlock Text="Option 1:" />
        <TextBox Text="{Binding Option1}" />
        <CheckBox IsChecked="{Binding Option2}" Content="Option 2" />
    </StackPanel>
</UserControl>
```

### Hookear al plugin

```csharp
public override ISettings GetSettings(bool firstRunSettings) => new TestPluginSettings(this);
public override UserControl GetSettingsView(bool firstRunSettings) => new TestPluginSettingsView();
```

### Flujo de settings

1. Playnite obtiene `GetSettings` y `GetSettingsView`.
2. El objeto `Settings` se establece como `DataContext` de la vista.
3. Se llama `BeginEdit`.
4. El usuario cancela → `CancelEdit`.
5. El usuario confirma → `VerifySettings` → si `true`, `EndEdit`.

---

## 7. Eventos

> https://api.playnite.link/docs/tutorials/extensions/events.html

### Eventos disponibles

| Nombre | Cuándo | Argumentos |
|---|---|---|
| `OnGameStarting` | Antes de iniciar el juego | `OnGameStartingEventArgs` |
| `OnGameStarted` | El juego ha arrancado | `OnGameStartedEventArgs` |
| `OnGameStopped` | El juego se ha detenido | `OnGameStoppedEventArgs` |
| `OnGameStartupCancelled` | Inicio del juego cancelado | `OnGameStartupCancelledEventArgs` |
| `OnGameInstalled` | Juego instalado | `OnGameInstalledEventArgs` |
| `OnGameUninstalled` | Juego desinstalado | `OnGameUninstalledEventArgs` |
| `OnGameSelected` | Selección de juego cambiada | `OnGameSelectedEventArgs` |
| `OnApplicationStarted` | Playnite ha arrancado | `OnApplicationStartedEventArgs` |
| `OnApplicationStopped` | Playnite cerrándose | `OnApplicationStoppedEventArgs` |
| `OnLibraryUpdated` | Biblioteca actualizada | `OnLibraryUpdatedEventArgs` |
| `OnControllerButtonStateChanged` | Botón de controlador pulsado/soltado | `OnControllerButtonStateChangedArgs` |
| `OnControllerConnected` | Controlador conectado | `OnControllerConnectedArgs` |
| `OnControllerDisconnected` | Controlador desconectado | `OnControllerDisconnectedArgs` |

> ℹ️ Los eventos de controlador no están disponibles para scripts PowerShell. En modo Escritorio, el soporte para controladores debe habilitarse en los ajustes de entrada.

### Cancelar inicio de juego

```csharp
public override void OnGameStarting(OnGameStartingEventArgs args)
{
    args.CancelStartup = true; // Cancela el inicio
}
```

### Ejemplo: Game Started / Stopped

```csharp
public override void OnGameStarted(OnGameStartedEventArgs args)
{
    logger.Info($"Game started: {args.Game.Name}");
}

public override void OnGameStopped(OnGameStoppedEventArgs args)
{
    logger.Info($"{args.Game.Name} ran for {args.ElapsedSeconds} seconds");
}
```

---

## 8. UI — Interacción con la interfaz de Playnite

> https://api.playnite.link/docs/tutorials/extensions/ui.html

### Juegos seleccionados

```csharp
var count = PlayniteApi.MainView.SelectedGames.Count;
PlayniteApi.Dialogs.ShowMessage($"Selected {count} games");
```

### Diálogos

```csharp
PlayniteApi.Dialogs.ShowMessage("Hello world!");
```

### Sidebar

Sobreescribir `GetSidebarItems`:

```csharp
public override IEnumerable<SidebarItem> GetSidebarItems()
{
    yield return new SidebarItem
    {
        Title = "Calculator",
        Icon = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "icon.png"),
        ProgressValue = 40,
        Activated = () => Process.Start("calc")
    };
}
```

**Tipos de item de Sidebar:**
- `Button`: botón de activación simple.
- `View`: botón de vista; `Opened` devuelve el control UI; `Closed` se llama al cambiar de vista.

El indicador de progreso se controla con `ProgressValue` y `ProgressMaximum` (valor `0` oculta la barra).

### Top Panel

Sobreescribir `GetTopPanelItems`:

```csharp
public override IEnumerable<TopPanelItem> GetTopPanelItems()
{
    yield return new TopPanelItem
    {
        Title = "Calculator",
        Icon = new TextBlock
        {
            Text = char.ConvertFromUtf32(0xeaf1),
            FontSize = 20,
            FontFamily = ResourceProvider.GetResource("FontIcoFont") as FontFamily
        },
        Activated = () => Process.Start("calc")
    };
}
```

### Formatos de icono soportados

Si `Icon` es `string`, Playnite lo interpreta en este orden:
1. Recurso de aplicación WPF con ese nombre.
2. Ruta absoluta a imagen.
3. Ruta relativa al tema actual.
4. Archivo de base de datos.

Si `Icon` es cualquier otro tipo, se asigna directamente como contenido (`Content`) del elemento UI (por ejemplo, un `TextBlock` para iconos de fuente).

---

## 9. Web Views

> https://api.playnite.link/docs/tutorials/extensions/webViews.html

Permiten usar un navegador Chromium (CEF) embebido. Útil para autenticación web.

```csharp
var view = PlayniteApi.WebViews.CreateView(640, 480);
view.Navigate("https://example.com");
view.OpenDialog(); // Abre ventana con el browser
```

**Tipos:**
- **Normal**: abre una ventana visible.
- **Offscreen**: instancia sin ventana, para acceso a recursos web sin interacción de usuario.

> ℹ️ Los datos de web view (caché, cookies) son compartidos entre todas las extensiones y el propio Playnite.  
> ℹ️ Usar `F12` para abrir las DevTools de Chromium mientras la ventana de web view está abierta.

---

## 10. Custom UI Integration

> https://api.playnite.link/docs/tutorials/extensions/customUiIntegration.html

Permite exponer controles WPF personalizados para que los temas los integren.

### Implementar un UserControl

Cambiar la clase base de `UserControl` a `PluginUserControl`:

```xml
<PluginUserControl x:Class="TestPlugin.TestPluginUserControl"
                   xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                   xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid />
</PluginUserControl>
```

```csharp
public partial class TestPluginUserControl : PluginUserControl
{
    public TestPluginUserControl() => InitializeComponent();
}
```

### Registrar el control

En el constructor del plugin:

```csharp
AddCustomElementSupport(new AddCustomElementSupportArgs
{
    ElementList = new List<string> { "TestUserControl1", "TestUserControl2" },
    SourceName = "TestPlugin"
});
```

Los temas referencian los controles como `TestPlugin_TestUserControl1`.

### Devolver el control

```csharp
public override Control GetGameViewControl(GetGameViewControlArgs args)
{
    if (args.Name == "TestUserControl1")
        return new TestPluginUserControl();
    return null;
}
```

### DataContext y GameContext

El juego actualmente enlazado se accede via `PluginUserControl.GameContext`. Para reaccionar a cambios, sobreescribir `GameContextChanged`.

### Exponer settings a temas

```csharp
AddSettingsSupport(new AddSettingsSupportArgs
{
    SourceName = "TestPlugin",
    SettingsRoot = "Settings" // Ruta relativa a la propiedad en la clase plugin
});
```

Los temas pueden referenciar settings via el markup `PluginSettings`.

### Exponer conversores personalizados

```csharp
AddConvertersSupport(new AddConvertersSupportArgs
{
    SourceName = "TestPlugin",
    Converters = new List<IValueConverter> { new MyConverter() }
});
```

Los temas referencian los conversores por el nombre de la clase.

### Información que el desarrollador de extensión debe proporcionar al de tema

- `SourceName`: para la integración UI y de settings.
- `ElementList`: lista de nombres de elementos expuestos.
- `Settings list`: si se exponen settings para temas.
- `Addon Id`: ID del addon del manifest.

---

## 11. URI Support

> https://api.playnite.link/docs/tutorials/extensions/uriSupport.html

Los plugins (no los scripts) pueden registrar handlers para URIs `playnite://`:

```csharp
PlayniteApi.UriHandler.RegisterSource("mysource", (args) =>
{
    // args.Arguments contiene los segmentos de la URL
    // playnite://mysource/arg1/arg2 → args.Arguments = ["arg1", "arg2"]
});
```

---

## 12. Debugging de scripts

> https://api.playnite.link/docs/tutorials/extensions/scriptingDebugging.html

### PowerShell ISE

1. Abrir PowerShell ISE (64-bit).
2. `Enter-PSHostProcess -Name Playnite.DesktopApp`
3. Identificar el runspace: `(Get-Runspace).Name`
4. `Debug-Runspace -Name "LibraryExporter.psm1"`
5. Usar `Debug → Toggle Breakpoint` y `Debug → Run/Continue`.

### Consola interactiva de PowerShell

Desde el menú principal de Playnite → Extensiones. Usar `CTRL-V + ENTER` para obtener las variables:
- `$PlayniteApi`: instancia de la API de Playnite.
- `$PlayniteRunspace`: runspace de PowerShell en el proceso de Playnite.

---

## 13. Localizaciones

> https://api.playnite.link/docs/tutorials/extensions/localizations.html

### Estructura

Crear directorio `Localization/` en la raíz de la extensión:

```
├── Localization/
│   ├── en_US.xaml   ← base (inglés)
│   ├── es_ES.xaml
│   └── {locale}.xaml
```

### Formato del archivo XAML

```xml
<?xml version="1.0"?>
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:sys="clr-namespace:System;assembly=mscorlib">
    <sys:String x:Key="LOCMyPluginSomeString">My string value</sys:String>
    <sys:String x:Key="LOCMyPluginMultiline" xml:space="preserve">Line 1
Line 2</sys:String>
</ResourceDictionary>
```

> ⚠️ Las claves de string son **globales** a todas las extensiones. Usar el prefijo `LOC<NombreExtensión>` para evitar conflictos. Formato recomendado: `LOC<extension_name>KeyValue`.

### Obtener strings localizados

En C#:
```csharp
var s = ResourceProvider.GetString("LOCMyPluginSomeString");
```

En XAML:
```xml
<TextBlock Text="{DynamicResource LOCMyPluginSomeString}" />
```

---

## 14. Manifest de extensión

> https://api.playnite.link/docs/tutorials/extensions/extensionsManifest.html

Archivo obligatorio: `extension.yaml` en el directorio raíz de la extensión.

### Propiedades

| Propiedad | Descripción |
|---|---|
| `Id` | Identificador único (string). No debe compartirse con ninguna otra extensión. |
| `Name` | Nombre de la extensión. |
| `Author` | Autor. |
| `Version` | Versión (string de versión .NET válido). |
| `Module` | Nombre del archivo DLL (plugins) o PSM1/PSD1 (scripts). |
| `Type` | Tipo: `Script`, `GenericPlugin`, `GameLibrary`, `MetadataProvider`. |
| `Icon` | Ruta relativa del icono (opcional). |
| `Links` | Lista de enlaces (opcional). |

### Ejemplo

```yaml
Id: ControllerSessionManager_6f3e7a21-98f4-4f2b-92ad-3fc0e6e941dc
Name: Controller Session Manager
Author: Narian
Version: 1.0.6
Module: ControllerSessionManager.dll
Type: GenericPlugin
Icon: media/icon.png
Links:
  - Name: GitHub
    Url: https://github.com/Naerian/playnite-nx-session-controller-manager
```

---

## 15. Search Support

> https://api.playnite.link/docs/tutorials/extensions/searchSupport.html

Playnite 10 introduce búsqueda global. Los plugins pueden integrarse de dos formas:

### Comandos globales de búsqueda

Sobreescribir `GetSearchGlobalCommands` en la clase del plugin.

### Contextos de búsqueda personalizados

Exponer contextos via la propiedad `Searches` del plugin:

| Propiedad | Descripción |
|---|---|
| `DefaultKeyword` | Palabra clave por defecto para activar el contexto. |
| `Name` | Nombre mostrado al navegar búsquedas disponibles. |
| `Context` | Objeto `SearchContext` que gestiona los resultados. |

### Implementar un SearchContext

```csharp
public class MySearchContext : SearchContext
{
    public MySearchContext()
    {
        Description = "Search description";
        Label = "My Search";
        Hint = "Hint text [F1]";
        Delay = 200; // ms de retardo tras el último carácter tecleado
    }

    public override IEnumerable<SearchItem> GetSearchResults(GetSearchResultsArgs args)
    {
        foreach (var game in API.Instance.Database.Games.Where(g => g.Name.Contains(args.SearchTerm)))
        {
            yield return new GameSearchItem(game, "Open", () => { /* action */ });
        }

        yield return new SearchItem("Custom item",
            new SearchItemAction("Do something", () => { /* action */ }))
        {
            Description = "Item description"
        };
    }
}
```

> ⚠️ `GetSearchResults` se ejecuta en un **hilo de fondo**. Aplicar restricciones de thread safety.

### Formatos de icono en items de búsqueda

- Ruta completa a imagen (local o HTTP).
- Clave de recurso string (BitmapImage o TextBlock).
- Ruta relativa a archivo de biblioteca.
- Cualquier elemento WPF.

### Abrir búsqueda desde SDK

```csharp
PlayniteApi.MainView.OpenSearch(myContext, "initial query");
```

---

## 16. Expanding Variables

> https://api.playnite.link/docs/tutorials/extensions/expandingVariables.html

Algunos campos de juego soportan variables dinámicas (por ejemplo `{InstallDir}`). Usar `ExpandGameVariables` para resolverlas:

```csharp
// Expandir en un string
var fullPath = PlayniteApi.ExpandGameVariables(game, @"{InstallDir}\app.exe");

// Expandir todos los campos de una GameAction
var expandedAction = PlayniteApi.ExpandGameVariables(game, game.GameActions[0]);
```

Si el string no contiene variables, `ExpandGameVariables` lo devuelve sin modificar.

---

## 17. Menús

> https://api.playnite.link/docs/tutorials/extensions/menus.html

Solo disponibles en modo **Desktop**.

```csharp
// Menú de juego
public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
{
    yield return new GameMenuItem
    {
        Description = "My menu item",
        Action = (itemArgs) =>
        {
            var games = itemArgs.Games;
            // ...
        }
    };
}

// Menú principal
public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
{
    yield return new MainMenuItem
    {
        Description = "My main menu item",
        MenuSection = "@",           // bajo "Extensions"
        // MenuSection = "@submenu"  // bajo "Extensions → submenu"
        // MenuSection = "@a|b"     // bajo "Extensions → a → b"
        Action = (itemArgs) => { /* ... */ }
    };
}
```

> ℹ️ `Get*MenuItems` se ejecuta **cada vez que el menú se abre**. No ejecutar código costoso aquí.

### Separadores

```csharp
new GameMenuItem { Description = "-" }
```

### Iconos de menú

- Ruta completa a imagen.
- Ruta relativa al tema.
- Clave de recurso de aplicación WPF.

---

## 18. Game Library (API de base de datos)

> https://api.playnite.link/docs/tutorials/extensions/library.html

Acceso via `PlayniteAPI.Database` (`IDatabaseAPI`).

### Obtener juegos

```csharp
foreach (var game in PlayniteApi.Database.Games) { /* ... */ }
var game = PlayniteApi.Database.Games[someGuid];
```

### Añadir juego

```csharp
var newGame = new Game("New Game");
PlayniteApi.Database.Games.Add(newGame);
```

### Modificar juego

```csharp
var game = PlayniteApi.Database.Games[someId];
game.Name = "Changed Name";
PlayniteApi.Database.Games.Update(game); // obligatorio para persistir
```

### Eliminar juego

```csharp
PlayniteApi.Database.Games.Remove(someId);
```

### Actualizaciones en bloque (recomendado)

```csharp
using (PlayniteApi.Database.BufferedUpdate())
{
    // Todos los cambios aquí no generan eventos individuales
}
// Aquí se genera un único evento con todos los cambios
```

### Campos de referencia

Los campos como `Series` solo se almacenan como referencias (`SeriesId`). Para cambiar el nombre de una serie:

```csharp
var series = PlayniteApi.Database.Series[someSeriesId];
series.Name = "New Name";
PlayniteApi.Database.Series.Update(series); // se propaga a todos los juegos
```

Para asignar una serie nueva a un juego:

```csharp
var newSeries = new Series { Name = "My Series" };
PlayniteApi.Database.Series.Add(newSeries);
game.SeriesIds = new List<Guid> { newSeries.Id };
PlayniteApi.Database.Games.Update(game);
```

### Eventos de cambio en colecciones

```csharp
PlayniteApi.Database.Games.ItemCollectionChanged += (_, args) =>
{
    logger.Info($"{args.AddedItems.Count} items added");
};
```

### Gestión de archivos (imágenes)

```csharp
// Exportar cover
var coverPath = PlayniteApi.Database.GetFullFilePath(game.CoverImage);

// Cambiar cover
PlayniteApi.Database.RemoveFile(game.CoverImage);
game.CoverImage = PlayniteApi.Database.AddFile(@"c:\new_cover.png", game.Id);
PlayniteApi.Database.Games.Update(game);
```

---

## 19. Ventanas personalizadas

> https://api.playnite.link/docs/tutorials/extensions/windows.html

Usar `CreateWindow` para que la ventana herede el tema de Playnite:

```csharp
var window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
{
    ShowMinimizeButton = false
});

window.Height = 600;
window.Width = 800;
window.Title = "My Window";
window.Content = new MyUserControl();
window.DataContext = new MyViewModel();
window.Owner = PlayniteApi.Dialogs.GetCurrentAppWindow();
window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
window.ShowDialog();
```

---

## 20. Logging

> https://api.playnite.link/docs/tutorials/extensions/logging.html

Los mensajes se escriben en `extensions.log` (`%AppData%\Playnite\` o directorio de instalación en modo portable).

```csharp
private static readonly ILogger logger = LogManager.GetLogger();

// Niveles disponibles: Trace, Debug, Info, Warn, Error
logger.Info("Plugin loaded");
logger.Warn("Something unexpected");
logger.Error(ex, "An error occurred");
```

> ℹ️ Los mensajes con severidad `Trace` solo se escriben si se habilita en `For developers` en los ajustes.

---

## 21. Toolbox utility

> https://api.playnite.link/docs/tutorials/toolbox.html

`Toolbox.exe` está incluido en el directorio de instalación de Playnite.

### Crear nueva extensión

```bash
# Script PowerShell
Toolbox.exe new PowerShellScript "My Script" "D:\plugins"

# Plugin genérico
Toolbox.exe new GenericPlugin "My Plugin" "D:\plugins"

# Plugin de biblioteca
Toolbox.exe new LibraryPlugin "My Library" "D:\plugins"

# Plugin de metadatos
Toolbox.exe new MetadataPlugin "My Metadata" "D:\plugins"
```

### Crear nuevo tema

```bash
Toolbox.exe new DesktopTheme "My Desktop Theme"
Toolbox.exe new FullscreenTheme "My Fullscreen Theme"
```

### Empaquetar extensión (generar `.pext` / `.pthm`)

```bash
# Plugin (pasar directorio con los binarios compilados)
Toolbox.exe pack "C:\plugin\bin\Release" "C:\output"
# → Genera: C:\output\PluginId_version.pext

# Tema
Toolbox.exe pack "C:\Playnite\Themes\Fullscreen\MyTheme" "C:\output"
# → Genera: C:\output\MyTheme.pthm
```

### Verificar manifests

```bash
Toolbox.exe verify addon "ruta\al\manifest.yaml"
Toolbox.exe verify installer "ruta\al\installer.yaml"
```
