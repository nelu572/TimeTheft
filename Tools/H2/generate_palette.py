import json, math
from pathlib import Path
s=Path(r'C:/Users/user/.codex/visualizations/2026/10/24/01a16d09-5242-7190-9176-07a49baf3c91/TimeTheft_H2_UnityHandoff')
p=json.loads((s/'Materials/C2_Palette39_Linear.json').read_text())['linear_rgb_actual_buffer']
def lab(v):
 r,g,b=v;l=(.4122214708*r+.5363325363*g+.0514459929*b)**(1/3);m=(.2119034982*r+.6806995451*g+.1073969566*b)**(1/3);ss=(.0883024619*r+.2817188376*g+.6299787005*b)**(1/3)
 return [.2104542553*l+.793617785*m-.0040720468*ss,1.9779984951*l-2.428592205*m+.4505937099*ss,.0259040371*l+.7827717662*m-.808675766*ss]
def arr(name,values):return 'static const float3 '+name+'['+str(len(values))+']={\n'+',\n'.join('float3('+','.join(format(x,'.10g') for x in c)+')' for c in values)+'\n};\n'
out='// Generated from delivered C2_Palette39_Linear.json, linear float32 palette.\n'+arr('C2Palette',[c for row in p for c in row])+arr('C2GoldLab',[lab(c) for c in p[0]])
Path('Assets/TimeTheft/PocketWatch/C2/Shaders/H2C2Palette.hlsl').write_text(out,encoding='utf-8')
