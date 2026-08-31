# Guía de publicación de Metadata AI

Esta guía describe el proceso completo para publicar una nueva versión de
Metadata AI: actualizar la versión y el instalador, ejecutar las pruebas,
generar el paquete `.pext`, subir el commit a GitHub y verificar la release
pública.

Todos los textos públicos de la release, incluido el `Changelog` de
`installer.yaml`, deben escribirse en inglés.

## Datos del proyecto

- Repositorio: `Naerian/playnite-nx-metadata-ia`
- Rama de publicación: `main`
- Add-on ID: `MetaDataIAPlugin_2f42c46c-9e3f-48cb-99b6-7f41f12d9b83`
- Ensamblado principal: `MetaDataIAPlugin.dll`
- API mínima de Playnite: `6.15.0`
- Script de release: `release.ps1`
- Script de empaquetado: `package.ps1`

## Método recomendado: script automatizado

La forma más rápida y segura es usar `release.ps1`. El script actualiza las
versiones, genera o actualiza `CHANGELOG.md` e `installer.yaml`, ejecuta todas
las pruebas, crea el `.pext` y verifica su contenido y hash.

Primero crea `.release-notes.md` con una línea por cambio, siempre en inglés:

```markdown
- Added the main new feature.
- Fixed the relevant bug.
- Improved metadata provider compatibility.
```

El fichero está ignorado por Git y se reutiliza para el changelog, el instalador
y las notas de GitHub. Si hay archivos nuevos de código, pruebas o documentación
para la release, revísalos y añádelos al área de staging antes de publicar; el
script no incorpora automáticamente archivos no versionados.

Preparar y validar sin publicar nada:

```powershell
.\release.ps1 -Version 1.4.15
```

La primera ejecución crea una plantilla de `.release-notes.md` si todavía no
existe. Tras editarla, vuelve a ejecutar el comando.

Cuando hayas revisado el diff, publicar la release completa:

```powershell
.\release.ps1 -Version 1.4.15 -Publish
```

Antes del commit y la publicación exige escribir `RELEASE 1.4.15`. Para una
ejecución no interactiva deliberada puede usarse `-Yes`:

```powershell
.\release.ps1 -Version 1.4.15 -Publish -Yes
```

Las secciones siguientes documentan el procedimiento manual equivalente y
sirven también para diagnosticar cualquier fallo del script.

## 1. Abrir el repositorio

```powershell
cd C:\Proyectos\playnite-nx-metadata-ia
```

## 2. Comprobar los requisitos y el estado inicial

```powershell
gh auth status
Test-Path C:\Playnite\Toolbox.exe
Test-Path C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe
gh release list --repo Naerian/playnite-nx-metadata-ia --limit 5
git status --short
git branch --show-current
```

Los dos `Test-Path` deben devolver `True` y la rama debe ser `main`. Antes de
continuar, identifica qué cambios pertenecen a la release. No añadas carpetas
locales no versionadas como `.cursor/`, ni documentación o archivos de trabajo
que no formen parte de la publicación.

Si GitHub CLI todavía no está autenticado:

```powershell
gh auth login
```

## 3. Elegir y configurar la versión

Ejemplo para la siguiente versión:

```powershell
$Version = "1.4.15"
$Tag = "v$Version"
$VersionForFile = $Version -replace '\.', '_'
$ReleaseDate = Get-Date -Format 'yyyy-MM-dd'
```

La versión se actualiza en cuatro lugares:

1. `extension.yaml`

   ```yaml
   Version: 1.4.15
   ```

2. `Properties\AssemblyInfo.cs`

   ```csharp
   [assembly: AssemblyVersion("1.4.15.0")]
   [assembly: AssemblyFileVersion("1.4.15.0")]
   ```

3. Añade una sección al principio de `CHANGELOG.md` (créalo con el título
   `# Changelog` si todavía no existe):

   ```markdown
   ## 1.4.15 — AAAA-MM-DD

   - Describe the main change in English.
   - Describe important fixes or compatibility improvements.
   ```

4. Añade una entrada al principio de `Packages` en `installer.yaml`:

   ```yaml
   - Version: 1.4.15
     RequiredApiVersion: 6.15.0
     ReleaseDate: AAAA-MM-DD
     PackageUrl: https://github.com/Naerian/playnite-nx-metadata-ia/releases/download/v1.4.15/MetaDataIAPlugin_2f42c46c-9e3f-48cb-99b6-7f41f12d9b83_1_4_15.pext
     Changelog:
       - "Describe the main change in English."
       - "Describe important fixes or compatibility improvements."
   ```

No elimines las versiones anteriores de `CHANGELOG.md` ni de `installer.yaml`.
El script automatizado reutiliza las mismas líneas de `.release-notes.md` en
ambos archivos para evitar divergencias.

Comprueba que no queden referencias accidentales a la versión anterior en los
archivos de publicación:

```powershell
rg -n --fixed-strings $Version `
    extension.yaml Properties\AssemblyInfo.cs CHANGELOG.md installer.yaml
```

## 4. Ejecutar las pruebas

```powershell
.\tests\run-vocabulary-behavior.ps1
.\tests\run-wikidata-payday3.ps1
```

Las dos pruebas deben finalizar correctamente. El empaquetado del paso siguiente
también compila la solución completa en configuración `Release`.

Si se añaden pruebas específicas para el cambio que se publica, ejecútalas y
menciónalas en las notas de GitHub.

## 5. Generar el paquete `.pext`

```powershell
.\package.ps1
```

El script:

1. Lee la versión desde `extension.yaml`.
2. Compila `MetaDataIAPlugin.sln` en `Release` con MSBuild de .NET Framework.
3. Comprueba las salidas requeridas.
4. Empaqueta desde un staging limpio usando `C:\Playnite\Toolbox.exe`.
5. Escribe el resultado en `dist\<versión>\` y muestra su SHA-256.

Guarda la ruta y el hash:

```powershell
$Package = Get-ChildItem "dist\$Version\*.pext" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
$LocalHash = (Get-FileHash -LiteralPath $Package.FullName -Algorithm SHA256).Hash

$Package.FullName
$Package.Length
$LocalHash
```

Comprueba que el paquete contiene los componentes esenciales:

```powershell
tar -tf $Package.FullName |
    Select-String "extension.yaml|MetaDataIAPlugin.dll|XamlAnimatedGif.dll|Icons/|Localization/|media/icon.png"
```

El paquete no debe incluir PDB, fuentes, claves, logs, cachés ni archivos de
configuración locales.

## 6. Revisar los cambios

```powershell
git diff --check
git diff --stat
git status --short
```

Revisa también el diff completo:

```powershell
git diff
```

Confirma especialmente que:

- Las cuatro ubicaciones de versión coinciden.
- La sección superior de `CHANGELOG.md` corresponde a la nueva versión.
- La primera entrada de `installer.yaml` usa la fecha, URL y versión correctas.
- El changelog público está en inglés.
- Solo se incluyen cambios destinados a esta release.
- No aparecen secretos, diagnósticos, paquetes generados ni configuración local.

## 7. Crear el commit y subirlo

Añade explícitamente los archivos revisados. No uses `git add -A` si el árbol
contiene directorios locales o archivos no relacionados.

```powershell
$ReleaseFiles = @(
    "extension.yaml"
    "Properties\AssemblyInfo.cs"
    "CHANGELOG.md"
    "installer.yaml"
    # Añade aquí cada archivo de código, prueba o documentación revisado.
)
git add -- $ReleaseFiles

git diff --cached --check
git diff --cached --stat
git commit -m "Release Metadata AI $Version"
git push origin main
```

Comprueba que el commit local y el remoto coinciden:

```powershell
git log -1 --oneline --decorate
git rev-parse HEAD
git rev-parse origin/main
git status --short
```

## 8. Preparar las notas de GitHub

Las notas deben estar en inglés, resumir los cambios relevantes y mencionar las
verificaciones ejecutadas. Incluye también el hash del artefacto.

```powershell
$NotesFile = Join-Path $env:TEMP "metadata-ai-$Version-release-notes.md"

@"
## What's Changed

- Describe the main feature or behavior change.
- Describe important fixes or compatibility improvements.

## Verification

- Vocabulary behavior tests passed.
- Wikidata/VNDB regression tests passed.
- Release build and Playnite Toolbox packaging completed successfully.

SHA-256: ``$LocalHash``
"@ | Set-Content -LiteralPath $NotesFile -Encoding UTF8

Get-Content -LiteralPath $NotesFile
```

Ajusta la sección `Verification` a las pruebas realmente ejecutadas; no anuncies
comprobaciones que no se hayan realizado.

## 9. Crear la release

```powershell
gh release create $Tag $Package.FullName `
    --repo Naerian/playnite-nx-metadata-ia `
    --target main `
    --title "Metadata AI $Version" `
    --notes-file $NotesFile
```

El comando crea el tag remoto, publica la release y sube el `.pext`. Cuando
termine:

```powershell
Remove-Item -LiteralPath $NotesFile
```

## 10. Verificar la release pública

```powershell
$Published = gh release view $Tag `
    --repo Naerian/playnite-nx-metadata-ia `
    --json url,isDraft,isPrerelease,tagName,targetCommitish,assets,publishedAt |
    ConvertFrom-Json

$Published.url
$Published.isDraft
$Published.isPrerelease
$Published.assets
```

La release debe tener:

- `isDraft` igual a `False`.
- `isPrerelease` igual a `False`, salvo que sea una beta deliberada.
- El tag y título esperados.
- Un único `.pext` con el nombre y la versión correctos.

Compara el hash local con el digest publicado por GitHub:

```powershell
$RemoteHash = (
    $Published.assets |
    Where-Object { $_.name -eq $Package.Name } |
    Select-Object -ExpandProperty digest
) -replace '^sha256:', ''

if ($LocalHash -ne $RemoteHash.ToUpperInvariant()) {
    throw "The public asset hash does not match the local package."
}

"Public asset hash verified: $LocalHash"
```

También puedes comprobar que la URL calculada en `installer.yaml` responde:

```powershell
$PackageUrl = "https://github.com/Naerian/playnite-nx-metadata-ia/releases/download/$Tag/$($Package.Name)"
(Invoke-WebRequest -UseBasicParsing -Method Head -Uri $PackageUrl).StatusCode
```

Debe devolver `200`.

## 11. Verificar el instalador público

Usa un parámetro en la URL para evitar una respuesta antigua de la caché:

```powershell
$InstallerUrl = "https://raw.githubusercontent.com/Naerian/playnite-nx-metadata-ia/main/installer.yaml?release=$Version"
$PublicInstaller = (Invoke-WebRequest -UseBasicParsing -Uri $InstallerUrl).Content

$PublicInstaller |
    Select-String -Pattern "Version: $Version|PackageUrl:"
```

La primera entrada debe ser la nueva versión y su `PackageUrl` debe coincidir
exactamente con el asset publicado.

## 12. Comprobación final

```powershell
git fetch --tags
git status --short
git log -1 --oneline --decorate
git tag --points-at HEAD
git rev-parse HEAD
git rev-parse origin/main
```

La publicación está cerrada correctamente cuando:

- El tag aparece sobre el commit esperado.
- `main` coincide con `origin/main`.
- No quedan cambios de la release sin subir.
- El `.pext` público tiene el mismo SHA-256 que el paquete local.
- El `installer.yaml` público anuncia la nueva versión y apunta al asset correcto.

Los archivos locales no relacionados que ya existieran antes de la release no
deben añadirse ni eliminarse como parte del proceso.

## Resumen rápido

Después de actualizar `extension.yaml`, `Properties\AssemblyInfo.cs`,
`CHANGELOG.md` e `installer.yaml`, el flujo mínimo es:

```powershell
$Version = "1.4.15"
$Tag = "v$Version"

.\tests\run-vocabulary-behavior.ps1
.\tests\run-wikidata-payday3.ps1
.\package.ps1

$Package = Get-ChildItem "dist\$Version\*.pext" | Select-Object -First 1
$LocalHash = (Get-FileHash -LiteralPath $Package.FullName -Algorithm SHA256).Hash

git diff --check
$ReleaseFiles = @(
    "extension.yaml"
    "Properties\AssemblyInfo.cs"
    "CHANGELOG.md"
    "installer.yaml"
    # Añade aquí el resto de archivos revisados para esta release.
)
git add -- $ReleaseFiles
git diff --cached --check
git commit -m "Release Metadata AI $Version"
git push origin main

# Crear y revisar $NotesFile antes de continuar.
gh release create $Tag $Package.FullName `
    --repo Naerian/playnite-nx-metadata-ia `
    --target main `
    --title "Metadata AI $Version" `
    --notes-file $NotesFile
```
