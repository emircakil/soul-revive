param(
    [Parameter(Mandatory)][string]$BuildDirectory,
    [Parameter(Mandatory)][string]$Destination,
    [Parameter(Mandatory)][ValidateSet('osx-arm64','osx-x64')][string]$Runtime
)
$ErrorActionPreference = 'Stop'
if (-not $IsMacOS) { throw 'Build the macOS native library on macOS.' }
# Official raylib 6.0, matching Raylib-cs 8.1.0. Pin the commit rather than a
# moving branch/tag; check the narrow GLFW change before applying it.
$commit = 'dbc56a87da87d973a9c5baa4e7438a9d20121d28'
$source = Join-Path $BuildDirectory 'source'
$build = Join-Path $BuildDirectory 'build'
& git init --quiet $source
if ($LASTEXITCODE -ne 0) { throw 'Cannot initialize native source directory.' }
& git -C $source fetch --quiet --depth 1 https://github.com/raysan5/raylib.git $commit
if ($LASTEXITCODE -ne 0) { throw 'Cannot fetch pinned raylib source.' }
& git -C $source checkout --quiet --detach FETCH_HEAD
if ($LASTEXITCODE -ne 0 -or (& git -C $source rev-parse HEAD).Trim() -ne $commit) { throw 'Unexpected raylib source revision.' }
$context = Join-Path $source 'src/external/glfw/src/nsgl_context.m'
$code = [IO.File]::ReadAllText($context)
$needle = '    ADD_ATTRIB(NSOpenGLPFAAccelerated);'
if (($code.Split($needle)).Length -ne 2) { throw 'The expected GLFW pixel-format constraint has changed.' }
$code = $code.Replace($needle, '    // Soul Revive: allow Apple software rendering when no accelerated renderer is available.')
[IO.File]::WriteAllText($context, $code)
$arch = if ($Runtime -eq 'osx-arm64') { 'arm64' } else { 'x86_64' }
# This 2D shell needs no GL 3.x features. A 2.1 context works with both Apple's
# hardware drivers and its software renderer on virtualized Macs. This exact
# library is shipped and tested; CI does not substitute a different renderer.
& cmake -S $source -B $build -DCMAKE_BUILD_TYPE=Release -DBUILD_SHARED_LIBS=ON -DBUILD_EXAMPLES=OFF -DOPENGL_VERSION=2.1 "-DCMAKE_OSX_ARCHITECTURES=$arch" -DCMAKE_OSX_DEPLOYMENT_TARGET=15.0
if ($LASTEXITCODE -ne 0) { throw 'Native raylib configure failed.' }
& cmake --build $build --config Release --parallel 3
if ($LASTEXITCODE -ne 0) { throw 'Native raylib build failed.' }
$library = Join-Path $build 'raylib/libraylib.dylib'
# Copy the file contents, not CMake's versioned symlink.
[IO.File]::WriteAllBytes($Destination, [IO.File]::ReadAllBytes($library))
& install_name_tool -id '@rpath/libraylib.dylib' $Destination
if ($LASTEXITCODE -ne 0) { throw 'Cannot set native library install name.' }
& lipo -verify_arch $arch $Destination
if ($LASTEXITCODE -ne 0) { throw 'Wrong native library architecture.' }
Copy-Item (Join-Path $source 'LICENSE') (Join-Path ([IO.Path]::GetDirectoryName($Destination)) 'RAYLIB-LICENSE.txt')
