# Fast compile check without Unity (about 10 s): builds the scripts with dotnet against the Unity-generated
# Firstlight.csproj (open the project in Unity once so it and Library/ScriptAssemblies exist).
# Writes _check.csproj (git-ignored) that also picks up script files Unity has not seen yet.
# Usage (Git Bash, from the project root): python tools/compile-check.py && dotnet build _check.csproj -nologo -v q
import re, sys, os
root = sys.argv[1] if len(sys.argv) > 1 else '.'
s = open(os.path.join(root, 'Firstlight.csproj'), encoding='utf-8-sig').read()
BS = chr(92)
s = re.sub(r'<ProjectReference Include="([^"]+)\.csproj" />',
           lambda m: '<Reference Include="%s"><HintPath>Library%sScriptAssemblies%s%s.dll</HintPath></Reference>' % (m.group(1), BS, BS, m.group(1)), s)
s = re.sub(r'\s*<Compile Include="Assets[^"]*" />', '', s)
s = s.replace('</Project>', '  <ItemGroup><Compile Include="Assets' + BS + 'Scripts' + BS + '**' + BS + '*.cs" Exclude="Assets' + BS + 'Editor' + BS + '**" /></ItemGroup>\n</Project>', 1)
s = s.replace('Temp' + BS + 'obj' + BS, 'Temp' + BS + 'checkobj' + BS).replace('Temp' + BS + 'bin' + BS + 'Debug' + BS, 'Temp' + BS + 'checkbin' + BS)
open(os.path.join(root, '_check.csproj'), 'w', encoding='utf-8').write(s)
print('wrote _check.csproj')
