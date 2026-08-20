# Guía de UI de ajustes (plugins Playnite Narian)

Referencia compartida para que los paneles de configuración de los plugins
luzcan igual: tipografía, espaciado, badges, botones, cards y expanders.

Implementación de referencia: `MetaDataIASettingsView.xaml` en
[playnite-nx-metadata-ia](https://github.com/Naerian/playnite-nx-metadata-ia).

Copiar los estilos con los mismos nombres de clave cuando sea posible. No inventar
un segundo sistema de chrome ni overrides globales de `Button`.

---

## Principios

1. **Tema de Playnite primero.** Usar brushes dinámicos del tema
   (`TextBrush`, `GlyphBrush`, `ControlBackgroundBrush`, `HoverBrush`,
   `PopupBackgroundBrush`, etc.). No inventar colores de acento propios salvo
   avisos críticos puntuales.
2. **Radio 4** en cards visuales, badges, expanders, pestañas y botones
   cuadrados (mismo lenguaje que el tema).
3. **Un solo estándar de botón** (ver sección Botones). No plantillas custom
   que sustituyan el `Button` del tema en toda la vista.
4. **SnapsToDevicePixels="True"** en borders/badges finos para evitar blur.
5. Raíz del `UserControl`: `FontSize="14"` y
   `TextElement.Foreground="{DynamicResource TextBrush}"`.

---

## Escala tipográfica fijada

| Uso | Tamaño |
| --- | --- |
| Títulos (páginas y secciones) | **20 px** |
| Texto normal y labels (inputs, dropdowns, checkboxes, pestañas, expanders) | **14 px** |
| Hints (ayudas bajo controles, notas, texto de badges) | **12 px** |

Estilos típicos:

- `SectionHeaderText` → 20, SemiBold, `GlyphBrush`
- `BodyText` / `FieldLabel` / cabeceras de tab / expander → 14, `TextBrush`
- `HintText` / texto dentro de badges → 12, `TextBrush` (hints con Opacity ~0.78)

---

## Escala de espaciado entre elementos

| Relación | Espacio |
| --- | --- |
| Dentro del campo (label → input) | **4 px** |
| Dentro del bloque (input → hint, checkbox → hint, botones en fila) | **8 px** |
| Entre campos del mismo bloque | **16 px** |
| Entre bloques (cards y secciones) | **24 px** |

Aplicación práctica en márgenes:

- `FieldLabel`: `Margin="0,0,0,4"`
- Hint bajo control: `Margin="0,8,0,16"` (8 tras el control; 16 cierra el campo)
- `SettingsCard` / bloques de sección: `Margin="0,0,0,24"`
- Botones icono en fila: margen derecho **8** entre botones
- Cabecera de sección (`PageHeader` / `SectionHeaderBorder`): padding inferior 8 + margen inferior **8** (título → label/contenido)
- Intro bajo título (`SectionSubtitleText`): margen inferior **16** (subtítulo → primer bloque)
- Si tras el título va un hint de sección, el `HintText` aporta su margen superior 8 → total título→hint ≈ 16

Contenido de página: margen exterior típico `16,16`.

### Títulos sin intro / hint

Si el título va **directo** a labels o controles (sin `SectionSubtitleText` ni hint de sección), el margen inferior de la cabecera debe ser **8**, no 16. Así no queda un hueco de “entre bloques” dentro del mismo card.

```
Título (20) ──8── label/control…
Título (20) ──8── subtítulo intro ──16── primer card/campo
Título (20) ──8── (+8 del HintText) hint de sección ──16── …
```

---

## Corner radius y superficies

- **CornerRadius = 4** en badges, chevrons, fills de expander/navegación y cards
  con fondo.
- Cards de contenido (`SettingsCard`): fondo transparente, sin borde; el
  espaciado (24) separa bloques. No envolver cada campo en una “card” con borde
  salvo que la interacción lo exija.
- Expander / ítem de navegación seleccionado: fill `ControlBackgroundBrush` o
  `HoverBrush`, acento izquierdo de 4 px con `GlyphBrush` al estar activo.

---

## Botones

### Estándar (acciones con texto)

Usar el **`Button` del tema de Playnite** sin `ControlTemplate` propio.

```xml
<Button Content="{DynamicResource MTDA_SomeAction}" Click="..."/>
```

No declarar un `<Style TargetType="Button" .../>` implícito que reemplace el
chrome del tema en toda la vista.

### Toolbar densa de iconos (`IconSquareButton`)

Solo para filas compactas (plantillas, acciones de lista, etc.):

- BasedOn tema `Button`
- Tamaño fijo **32×32** (Min/Max iguales)
- `Padding="0"`, margen derecho **8** (el último de la fila puede poner `Margin="0"`)
- Contenido: `Viewbox` **16×16** + `Path` stroke `TextBrush`, thickness 2, caps Round
- Etiqueta accesible vía `ToolTip` (obligatorio si no hay texto)

```xml
<Button Style="{StaticResource IconSquareButton}"
        ToolTip="{DynamicResource MTDA_New}"
        Click="AddTemplate_OnClick">
    <Viewbox Width="16" Height="16" Stretch="Uniform"
             HorizontalAlignment="Center" VerticalAlignment="Center">
        <Path Data="M12,5 L12,19 M5,12 L19,12"
              Stroke="{DynamicResource TextBrush}" StrokeThickness="2"
              StrokeStartLineCap="Round" StrokeEndLineCap="Round"/>
    </Viewbox>
</Button>
```

### Chevron de expander (`IconSquareButton`)

**El mismo estilo** que la toolbar de Plantillas: `Button` + `IconSquareButton`
(no `ToggleButton`). El click alterna `IsExpanded` del `Expander` padre
(handler en code-behind).

- Icono Path chevron: cerrado `M6,9 L12,15 L18,9`, abierto `M6,15 L12,9 L18,15`
- El área de título sigue siendo un `ToggleButton` transparente
  (`ExpanderHeaderButton`) enlazado a `IsExpanded`
- El Path del chevron cambia con el trigger `IsExpanded` del template

### Excepciones permitidas

- **Token copy / celdas de tabla:** botón mínimo sin chrome de toolbar.
- **Overlays transparentes** de hit-test (p. ej. prioridad de fuentes).
- **Cabecera de expander** transparente (solo hit area, no “botón” visual).

No crear un tercer estilo de botón “bonito” con template propio para acciones
normales.

---

## Badges

Estilo base `SummaryPill` (con borde) para **estado**. Las variantes que no
son de estado **no llevan borde**.

### Base / estado (`SummaryPill`, `SourceStatusBadge`)

| Propiedad | Valor |
| --- | --- |
| Contenedor | `Border` |
| Fondo | `ControlBackgroundBrush` |
| Borde | `GlyphBrush`, **1 px** (solo estado) |
| Radio | **4** |
| Padding | **8, 2** |
| Alto mínimo | **22** |
| Texto | `TextBrush`, **12 px**, centrado |
| SnapsToDevicePixels | `True` |

### Variantes

| Variante | Clave sugerida | Diferencias vs base |
| --- | --- | --- |
| Estado (Overview, Fuentes) | `SourceStatusBadge` o `SummaryPill` + margen | Margen **`8,0,0,0`** (a la derecha del texto/título). **Con borde.** |
| Capacidad u otros no-estado | `SourceCapabilityBadge` | **`BorderThickness="0"`**, padding **`6,1`**, margen **`0,0,8,0`**, Opacity **0.92** |

### Colores de estado

Los badges de estado **sí** usan color propio del tema, y **texto y borde
comparten el mismo brush**:

| Estado | Brush (texto + borde) |
| --- | --- |
| Activo / listo / OK | `PositiveRatingBrush` |
| Aviso / falta config / credenciales | `WarningBrush` |
| Inactivo | `GlyphBrush` (opacidad ~0.65) |

No dejar el borde en `GlyphBrush` cuando el texto ya va en Positive/Warning.
Aplicar ambos en code-behind (p. ej. `ApplyStatusBadgeAppearance`).

Los badges neutrales (contadores, labels sin semántica de estado) mantienen
texto `TextBrush` y borde `GlyphBrush` por defecto.

### Markup típico

```xml
<Border Style="{StaticResource SummaryPill}">
    <TextBlock Text="Activo"/>
</Border>
```

Estado junto a un título:

```xml
<StackPanel Orientation="Horizontal" VerticalAlignment="Center">
    <TextBlock Text="Steam" Style="{StaticResource MediaSourceHeaderText}"/>
    <Border Style="{StaticResource SourceStatusBadge}">
        <TextBlock Text="Activo"/>
    </Border>
</StackPanel>
```

Fila de capacidades:

```xml
<StackPanel Orientation="Horizontal" Margin="0,8,0,0">
    <Border Style="{StaticResource SourceCapabilityBadge}">
        <TextBlock Text="Metadata"/>
    </Border>
    <Border Style="{StaticResource SourceCapabilityBadge}">
        <TextBlock Text="Media"/>
    </Border>
</StackPanel>
```

---

## Expanders

- Estilo `SettingsExpander` (y variantes como `MediaSourceExpander` si hace falta
  separador o header compuesto).
- Cabecera: fill redondeado 4, acento izquierdo al expandir, título 14 SemiBold
  cuando está abierto.
- Hint de “pulsa para expandir” a 12 px; se oculta al expandir.
- Contenido: padding típico `16,16,8,16`, `FontSize` 14.
- Chevron: **`IconSquareButton`** (mismo que Plantillas; ver Botones).

---

## Pestañas / navegación lateral

- Labels **14 px**.
- Ítem: altura mínima ~44, hover/selección con `HoverBrush`, acento 4 px
  `GlyphBrush` si es navegación vertical.
- Iconos de tab (Segoe Fluent / MDL2): ~16 px, margen derecho 8.

---

## Formularios (checklist rápido)

Para cada campo:

1. Label (`FieldLabel`) → 4 px → control
2. Si hay ayuda: 8 px → `HintText` (12 px)
3. Siguiente campo del mismo bloque: cerrar con 16 px (suele ir en el margen
   inferior del hint)
4. Siguiente card/sección: 24 px

Checkboxes: título 14; hint debajo con el mismo ritmo 8 / 16.

---

## Qué no hacer

- Override implícito de todos los `Button` con un `ControlTemplate` custom.
- Mezclar botones “tema”, botones “pastilla propia” y botones icono sin criterio.
- Badges de estado con texto en color y borde en `GlyphBrush`: texto y borde
  deben compartir el mismo brush (`PositiveRatingBrush` / `WarningBrush` /
  `GlyphBrush`). No hardcodear hex fuera del tema.
- Tamaños tipográficos fuera de 20 / 14 / 12.
- Márgenes “a ojo” (6, 10, 12, 18…) que rompan la escala 4 / 8 / 16 / 24.
- Cards anidadas con borde solo por estética.

---

## Checklist al portar a otro plugin

- [ ] Raíz 14 px + `TextBrush`
- [ ] Títulos 20, cuerpo 14, hints/badges 12
- [ ] Espaciado 4 / 8 / 16 / 24
- [ ] `SummaryPill` + variantes de margen/padding
- [ ] Acciones de texto = Button del tema
- [ ] Toolbars = `IconSquareButton` 32 + icono 16 + tooltip
- [ ] Chevron expander = `IconSquareButton` (igual que toolbar)
- [ ] Sin estilo implícito global de `Button`
- [ ] Solo brushes del tema (+ radio 4)
