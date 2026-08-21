# Guía de UI de ajustes (plugins Playnite Narian)

Sistema visual unificado para paneles de configuración de plugins Narian. Chrome **propio del plugin**: no depende del tema de Playnite para verse bien.

- **Diseño Figma:** [Narian Plugins — Settings Design System](https://www.figma.com/design/0ilhUvo6xBoEkEldkdP3Wh/Narian-Plugins-%E2%80%94-Settings-Design-System)
- **Implementación de referencia:** este repo — `SettingsAppearance.cs`, `SettingsChrome.xaml`, `MetaDataIASettingsView.xaml`
- **Alcance:** Metadata AI es la referencia; el mismo sistema se reutiliza en el resto de plugins Narian.

---

## Principios

1. **Chrome propio.** El panel pinta fondo, texto, bordes, acentos, inputs, botones, tabs, nav, cards, badges y scrollbar.
2. **Estructura fija, color variable.** Tipografía, espaciado, radios y tamaños de control son iguales en todos los presets. Solo cambian los colores.
3. **El host no debe asomar.** El root del settings view rellena el área del diálogo. Un preset oscuro no deja ver el tema claro de Playnite detrás (ni al revés). Best-effort: pintar también fondo de ventana host y botones Guardar/Cancelar del pie.
4. **Presets solo de color.** Midnight · Paper · OLED · Ocean · Ember. Persistidos en settings (`AppearancePreset`).
5. **Default:** Midnight (hasta detectar tema claro del host y sugerir Paper).

---

## Tokens de estructura (fijos)

No cambian entre presets.

### Tipografía

| Uso | Tamaño | Peso |
| --- | --- | --- |
| Títulos de página / sección (`SectionHeaderText`) | **20 px** | SemiBold · color `accent` |
| Texto, labels, tabs, nav, expanders | **14 px** | Regular / SemiBold en activo |
| Hints y texto de badges | **12 px** | Italic · `textMuted` / `GlyphBrush` |

### Espaciado (escala 4)

| Relación | Espacio |
| --- | --- |
| Label → control | **4 px** |
| Control → hint; gap entre botones/chips | **8 px** |
| Entre campos del mismo bloque | **16 px** |
| Entre cards / secciones | **24 px** |
| Padding de ElevatedCard / SummaryCard | **16 px** (formularios: 0) |
| Padding de página / márgenes de contenido | **8–16 px** |

Cabecera de sección dentro de card (`SectionHeaderBorder` / `SummaryTitleSeparator`):

- Título 20 + línea inferior con el **mismo** color que el borde de la card (`Narian.Border` / token `border`)
- No usar `GlyphBrush` ni accent en esa línea
- Padding inferior **8**, margen inferior **8** hacia el contenido

Hints (`FieldHintText` / `HintText` / intros bajo título):

- **Cursiva**, 12 px, `textMuted` / `GlyphBrush`
- Margen **`0,8,0,16`** (alineados a la izquierda; **sin** indent 24)
- `SectionSubtitleText` (intro de página): también italic + opacity 0.78

### Radios y tamaños

| Token | Valor |
| --- | --- |
| Radio estándar | **4 px** (inputs, botones, chips, badges, cards, nav, scrollbar thumb) |
| Alto control (TextBox, PasswordBox, ComboBox, Button) | **36 px** |
| Padding interno de control (TextBox, PasswordBox, ComboBox editable/no editable) | **6,0** |
| Padding TextArea (`AcceptsReturn`) | **6,8** |
| IconSquare | **32×32**, icono interno **16×16**, margen derecho **8** |
| Tab horizontal | alto **40 px** |
| Nav vertical | alto ítem **44 px** |
| Badge | min height **22**, padding **8,2** (estado) / **6,1** (capacidad) |

`SnapsToDevicePixels="True"` en borders finos. Cursor `Hand` en botones/chips/tabs clickables.

---

## Tokens de color (por preset)

### Nombres semánticos (recursos XAML)

| Token recurso | Uso |
| --- | --- |
| `Narian.Bg` | Fondo shell / página |
| `Narian.Surface` | Cards, inputs, tabs bar, superficies elevadas |
| `Narian.Hover` | Hover de controles / filas |
| `Narian.Selected` | Selección (tabs, nav, filas) |
| `Narian.Accent` | Primario: títulos de sección, tab activo, chip activo, botón primario, focus |
| `Narian.AccentHover` | Hover del primario / acento |
| `Narian.AccentOn` | Texto/icono sobre `accent` |
| `Narian.Text` | Texto principal (también override de `TextBrush`) |
| `Narian.TextMuted` | Hints, secundarios (también `GlyphBrush`) |
| `Narian.Border` | Bordes de input, cards, divisores |
| `Narian.Success` / `PositiveRatingBrush` | Semántica OK |
| `WarningBrush` | Semántica aviso |
| `Narian.RowOdd` / `RowEven` / `TableHeader` | Tablas |
| `Narian.BadgeBg` | Badge neutro (sin estado) |
| `Narian.BadgeSuccessBg` | Fondo soft del badge OK |
| `Narian.BadgeWarningBg` | Fondo soft del badge aviso |
| `Narian.BadgeMutedBg` | Fondo soft del badge inactivo / muted |

También se sobreescriben brushes de Playnite en el `ResourceDictionary` del settings view (`ControlBackgroundBrush` → Surface, etc.) para que markup existente herede el preset.

### Regla Field

El bloque Field (label + control + hint) **no** lleva un rectángulo `surface` propio: vive sobre el fondo de la card/`bg`. Solo el control interno usa `surface` + `border`.

### Regla `accentOn`

- Accent claro (Midnight, OLED, Ocean, Ember): texto primario **oscuro** `#0B0D12`
- Accent saturado (Paper): texto primario **blanco** `#FFFFFF`

### Mapas de preset

#### Midnight (default)

| Token | Hex |
| --- | --- |
| `bg` | `#12151C` |
| `surface` | `#1A1F2A` |
| `hover` | `#242B3A` |
| `selected` | `#2A3348` |
| `accent` | `#6EA8FF` |
| `accentHover` | `#8BBBFF` |
| `accentOn` | `#0B0D12` |
| `text` | `#EEF1F6` |
| `textMuted` | `#8B93A7` |
| `border` | `#2A3140` |
| `success` | `#3DDC97` |
| `warning` | `#E6B84D` |
| `rowOdd` | `#161A22` |
| `rowEven` | `#1A1F2A` |
| `tableHeader` | `#222836` |
| `badgeBg` | `#242B3A` |
| `badgeSuccessBg` | `#1B3A2E` |
| `badgeWarningBg` | `#3A3220` |
| `badgeMutedBg` | `#2A3140` |

#### OLED

| Token | Hex |
| --- | --- |
| `bg` | `#000000` |
| `surface` | `#0A0A0A` |
| `hover` | `#161616` |
| `selected` | `#1E1E1E` |
| `accent` | `#6EA8FF` |
| `accentHover` | `#8BBBFF` |
| `accentOn` | `#0B0D12` |
| `text` | `#F2F2F2` |
| `textMuted` | `#9A9A9A` |
| `border` | `#222222` |
| `success` | `#3DDC97` |
| `warning` | `#E6B84D` |
| `rowOdd` | `#050505` |
| `rowEven` | `#0A0A0A` |
| `tableHeader` | `#141414` |
| `badgeBg` | `#161616` |
| `badgeSuccessBg` | `#0F2A20` |
| `badgeWarningBg` | `#2A2414` |
| `badgeMutedBg` | `#1E1E1E` |

#### Ocean

| Token | Hex |
| --- | --- |
| `bg` | `#0E151C` |
| `surface` | `#15202B` |
| `hover` | `#1C2B3A` |
| `selected` | `#243648` |
| `accent` | `#3DDCB4` |
| `accentHover` | `#5FE6C4` |
| `accentOn` | `#0B0D12` |
| `text` | `#E8F1F7` |
| `textMuted` | `#8AA0B0` |
| `border` | `#243040` |
| `success` | `#3DDC97` |
| `warning` | `#E6B84D` |
| `rowOdd` | `#101820` |
| `rowEven` | `#15202B` |
| `tableHeader` | `#1A2836` |
| `badgeBg` | `#1C2B3A` |
| `badgeSuccessBg` | `#16362C` |
| `badgeWarningBg` | `#35301C` |
| `badgeMutedBg` | `#243040` |

#### Ember

| Token | Hex |
| --- | --- |
| `bg` | `#161311` |
| `surface` | `#1F1A16` |
| `hover` | `#2A231E` |
| `selected` | `#332B24` |
| `accent` | `#E8A05C` |
| `accentHover` | `#F0B57A` |
| `accentOn` | `#0B0D12` |
| `text` | `#F3EEE8` |
| `textMuted` | `#A89888` |
| `border` | `#3A3028` |
| `success` | `#3DDC97` |
| `warning` | `#E6B84D` |
| `rowOdd` | `#1A1613` |
| `rowEven` | `#1F1A16` |
| `tableHeader` | `#26201B` |
| `badgeBg` | `#2A231E` |
| `badgeSuccessBg` | `#1E3428` |
| `badgeWarningBg` | `#3A2E1C` |
| `badgeMutedBg` | `#3A3028` |

#### Paper (claro)

| Token | Hex |
| --- | --- |
| `bg` | `#F7F8FA` |
| `surface` | `#FFFFFF` |
| `hover` | `#EEF1F6` |
| `selected` | `#E4ECFB` |
| `accent` | `#3B6FE8` |
| `accentHover` | `#2F5FD4` |
| `accentOn` | `#FFFFFF` |
| `text` | `#1A1F2A` |
| `textMuted` | `#5C6578` |
| `border` | `#D5DAE3` |
| `success` | `#1B8A5A` |
| `warning` | `#B8860B` |
| `rowOdd` | `#FFFFFF` |
| `rowEven` | `#F3F5F8` |
| `tableHeader` | `#E8ECF2` |
| `badgeBg` | `#EEF1F6` |
| `badgeSuccessBg` | `#E3F5EC` |
| `badgeWarningBg` | `#F7F0D9` |
| `badgeMutedBg` | `#E8ECF2` |

---

## Anatomía del shell

```
┌─ Shell (bg) ─────────────────────────────────────────────┐
│ Top tabs (surface) · activo: selected fill + accent text │
├──────────────┬───────────────────────────────────────────┤
│ Left nav     │ Content (bg)                              │
│ (surface)    │  Page header 20 / intro textMuted         │
│ item sel =   │  SectionHeader (accent 20)                │
│ selected     │  Fields / tables / actions on bg          │
│              │  ElevatedCard solo Overview + About       │
│              │  Badges: neutros o tintados por estado    │
├──────────────┴───────────────────────────────────────────┤
│ Advanced mode checkbox (derecha) · Save/Cancel host      │
└──────────────────────────────────────────────────────────┘
```

- Wrappers de layout / formularios (`SettingsCard`): **transparentes** sobre `bg` (sin surface ni borde). Patrón Audio Switcher.
- Elevación (`ElevatedCard` / `SummaryCard`): solo resumen Overview y bloque narrativo About (`surface` + `border` 1 + radio 4 + padding 16).
- Agrupar campos del mismo dominio bajo **un SectionHeader** (no partir selector + credenciales + test).

---

## Componentes

### Botón primario (`NarianPrimaryButton`)

- Alto **36**, radio **4**, fill `accent`, foreground `accentOn`.
- Hover: `accentHover` (cambio de fill, **no** borde extra).

### Botón secundario

- Fill `surface`, texto `text`, borde `border`.
- Hover: fill `hover` (sin borde de acento).

### IconSquareButton

- **32×32**, padding 0, margen derecho **8**.
- Icono Path/Viewbox **16×16**, stroke `text`.
- `ToolTip` obligatorio si no hay texto.

### Chips de Appearance preset

- Alto **36**, radio **4**, padding horizontal **12**.
- Inactivo: fondo `badgeBg`, texto `text`, borde `border`.
- Activo: fondo `accent`, texto `accentOn`, borde `accent`.
- Hover inactivo: `hover`. Activo **no** cambia en hover.
- Solo cambian colores; tipografía/spacing fijos.

### Inputs (TextBox, PasswordBox, ComboBox, ListBox, DatePicker)

- Alto / MinHeight / MaxHeight **36** (PasswordBox incluido).
- Padding interno unificado **`6,0`** en TextBox, PasswordBox y ComboBox (misma medida visual).
- Fill `surface` (`ControlBackgroundBrush`), borde `border`, radio **4**.
- Focus: borde `accent`. Hover: fondo `hover`.
- TextArea (`AcceptsReturn`): MinHeight mayor, padding **`6,8`**, content top.
- Flecha de ComboBox centrada en columna **36** px; clic en todo el control abre el dropdown (no solo la flecha).
- ComboBox editable: template con `PART_EditableTextBox` (mismo padding `6,0`).

### CheckBox / RadioButton

- Caja/círculo alineados al preset; checkmark centrado.
- Checked: fill/stroke `accent`.

### Badges

Misma forma en todos: radio **4**, sin sombra, sin borde (o borde 0).

| Tipo | Fondo | Texto |
| --- | --- | --- |
| Neutro (conteos, capacidades, “Predeterminada”…) | `BadgeBg` | `text` / default |
| Estado OK / Active / Ready | `BadgeSuccessBg` | `success` |
| Estado aviso / Needs config | `BadgeWarningBg` | `warning` |
| Inactivo / muted | `BadgeMutedBg` | `textMuted` |

- Estado: padding **8,2**, min height **22**
- Capacidad (Metadata / Media): padding **6,1**, margen derecho **8**

### Tabs superiores

- Alto **40**, barra con fondo `surface`, radio **4**.
- Hover: fill `hover`. Seleccionado: fill `selected`, texto/icono `accent`, SemiBold.

### Nav vertical

- Ítem alto **44**.
- Hover / seleccionado: mismos tokens que tabs (`hover` / `selected` + `accent`).

### Secciones y cabeceras

```
PageSection / SettingsCard (flat)
  └─ PageHeader o SectionHeaderBorder + SectionHeaderText (accent, 20)
  └─ campos… (sobre bg; sin marco surface)

ElevatedCard / SummaryCard  → solo Overview tiles y About narrativo
  └─ SummaryTitleSeparator: línea inferior = borde de la card (`Narian.Border`)
```

### Tablas

- Header: `tableHeader`
- Filas: alternar `rowOdd` / `rowEven`
- Celdas con borde sutil `border`
- Sin “marco blando” externo extra alrededor de la tabla (el scroll va directo)

### ScrollBar

- Ancho/alto track **10**, radio **4**
- Track: `rowOdd` · Thumb: `border`
- Debe adaptarse al preset (no dejar la scrollbar del tema Playnite)

### Expanders

- Cabecera radio 4; hover/expand con `hover`/`surface`
- Contenido alineado al resto de fields (hints a la izquierda)

---

## Selector de Appearance

Fila de chips en Overview (o equivalente):

`Midnight` · `Paper` · `OLED` · `Ocean` · `Ember`

- Hint: colores únicamente; type/spacing/tamaños fijos.
- Persistencia: `AppearancePreset` en settings del plugin.
- Aplicación: `SettingsAppearance.Apply(root, preset)` al cargar y al cambiar chip.
- Host chrome (best-effort): fondo de ventana, DWM title bar, Save/Cancel del diálogo.

---

## Arquitectura de implementación (referencia)

| Pieza | Rol |
| --- | --- |
| `SettingsAppearance.cs` | Paletas, `Apply`, override de brushes, host chrome |
| `SettingsChrome.xaml` | Estilos implícitos: TextBox, PasswordBox, ComboBox, Button, CheckBox, Radio, ListBox, ScrollBar + brushes default Midnight |
| `*SettingsView.xaml` | Layout, cards, tabs, nav, badges; merged dictionary → SettingsChrome |
| Setting `AppearancePreset` | Persistencia |

Patrón al abrir settings:

1. `InitializeComponent`
2. `SettingsAppearance.Apply(this, settings.AppearancePreset)`
3. Construir chips de preset y refrescar selección
4. Best-effort `ApplyHostChrome`

Compilar con Framework MSBuild (`package.ps1` / `C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe`), no asumir `dotnet build` para net462.

---

## Qué no hacer

- Dejar frames internos en blanco dentro de presets oscuros.
- Field con fill `surface` si incluye label+hint (bloque distinto).
- Tipografía fuera de **20 / 14 / 12** o márgenes fuera de **4 / 8 / 16 / 24**.
- PasswordBox u otro input con altura distinta de **36**, o padding distinto de **6,0** (textarea **6,8**).
- ComboBox que solo abra con la flecha o editable sin `PART_EditableTextBox`.
- Badges con DropShadow o borde de color distinto al texto (usar fondos tintados).
- Hints con indent izquierdo (`Margin="24,…"`).
- Envolver formularios enteros en cards elevadas (reservar elevación a Overview/About).
- Partir en varias secciones lo que es un mismo dominio (p. ej. proveedor + API key + test).
- Depender del tema Playnite para contraste del chrome propio.
- Tercer estilo de botón fuera de primary / secondary / IconSquare.

---

## Ventanas propias del plugin (Wizard)

El asistente de primera configuración (`SetupWizardWindow`) usa el mismo chrome:

1. `SettingsAppearance.ApplyWindow(window, AppearancePreset)`
2. `WindowStyle=None`, borde Narian, footer **Skip** a la izquierda / **Back+Next** a la derecha
3. Título de paso **20** `accent`; step label **12** `textMuted`
4. Botón Next/Finish con `IsDefault` → estilo primario
5. Resumen con filas `WizardSummaryRow` (label muted + value), no un bloque de texto plano
6. Entrada en **Avanzado → Configuración inicial** (`MTDA_TabSetup` / `MTDA_OpenSetupWizard`)
7. `SetupWizardCompleted` + migración para no forzar el wizard en instalaciones viejas

### Posicionamiento (ancla Playnite)

- `WindowStartupLocation=Manual` (no `CenterOwner`)
- Ancla de posición: `Application.Current.MainWindow` (no el `Owner`; el diálogo de ajustes puede ser pequeño/desplazado)
- `Owner = GetCurrentAppWindow()` solo para modalidad / z-order
- Si el ancla está maximizado: centrar en el **work area del monitor del ancla** (`Screen.FromHandle`); nunca usar `Left`/`Top` del ancla (son restore bounds)
- Si no está maximizado: `PointToScreen` del centro del ancla + conversión a DIP
- Clamp al work area del monitor del ancla (no `SystemParameters.WorkArea` del monitor principal)
- Recentrar en `Loaded`, `SizeChanged`, cambio de paso y `ApplicationIdle`; respetar `userMovedWindow` tras `DragMove` en la cabecera
- Referencias: `System.Windows.Forms` + `System.Drawing` si se usa `Screen`

No modifica juegos; solo configuración.

---

## Checklist al portar a otro plugin

- [ ] Copiar/adaptar `SettingsAppearance.cs` + `SettingsChrome.xaml`
- [ ] Root UserControl: fondo `bg`, stretch completo
- [ ] Tokens estructura: type 20/14/12, space 4/8/16/24, radio 4, control 36, IconSquare 32, tab 40, nav 44
- [ ] Cinco presets con el mapa de tokens (incl. badge tintados)
- [ ] Formularios planos (SectionHeader sobre bg); ElevatedCard solo Overview/About
- [ ] Inputs/PasswordBox/ComboBox misma altura (**36**), radio (**4**) y padding (**6,0**; textarea **6,8**)
- [ ] ComboBox: clic en toda el área + `PART_EditableTextBox` si es editable
- [ ] Badges: neutros `BadgeBg`; estado con `Badge*Bg` + foreground semántico; sin sombra
- [ ] ScrollBar del preset
- [ ] Appearance chips + `AppearancePreset` persistido
- [ ] Host chrome best-effort (ventana + Save/Cancel)
- [ ] Hints alineados a la izquierda
- [ ] Sin pelear con estilos globales de Playnite (overrides en el ResourceDictionary del view)

---

## Referencias

| Recurso | Ubicación |
| --- | --- |
| Figma — Overview + kit | página `00` |
| Figma — Color presets | [node 2:42](https://www.figma.com/design/0ilhUvo6xBoEkEldkdP3Wh/Narian-Plugins-%E2%80%94-Settings-Design-System?node-id=2-42) |
| Figma — Settings shells ×5 | [node 3:2](https://www.figma.com/design/0ilhUvo6xBoEkEldkdP3Wh/Narian-Plugins-%E2%80%94-Settings-Design-System?node-id=3-2) |
| Paletas / Apply | `SettingsAppearance.cs` |
| Estilos de control | `SettingsChrome.xaml` |
| Vista de referencia | `MetaDataIASettingsView.xaml` |
