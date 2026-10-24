"""승인된 계수기의 최종 픽셀 표면과 편집 레이어를 재생성한다.

메시/UV/Unity 상태는 변경하지 않는다. 512px 아틀라스, 4texel 논리 픽셀,
무광 소재 팔레트와 기존 도관 색면을 고정한 최종 제작기다.
"""
from pathlib import Path
import argparse
import hashlib
import json
import xml.etree.ElementTree as ET
import zipfile
from io import BytesIO

import numpy as np
from PIL import Image, ImageDraw, ImageFont

PROJECT = Path(__file__).resolve().parents[2]
ROOT = PROJECT / 'Assets/TimeTheft/Counter'
UV = ROOT / 'Source/UV'



RAMPS = {'green': ['#485746', '#829175', '#aeb99a', '#bfc2a5'], 'brown': ['#574233', '#947759', '#baa07a', '#c6b18d'], 'brass': ['#4e4330', '#827252', '#aa9365', '#baa57c'], 'bronze': ['#3e4436', '#697057', '#93977b', '#a5a78c'], 'steel': ['#424e49', '#788679', '#929e8b', '#a5ae99'], 'rubber': ['#262b29', '#424a43', '#535b4f'], 'redrubber': ['#4f3332', '#805349', '#916354'], 'auxrubber': ['#342d2d', '#5c4540', '#71524b'], 'screen': ['#090f10', '#101817']}


MATERIAL = {'GreyGreenCastSteel': 'green', 'BrownFoldedSheet': 'brown', 'DullBrassClamps': 'brass', 'DarkBronzeProtection': 'bronze', 'RecessWallSteel': 'bronze', 'UnlitDarkDisplayBacking': 'screen', 'GasketsAndCable': 'rubber', 'FastenerSteel': 'steel'}


def rgb(text):
    return tuple(int(text[i:i+2],16) for i in (1,3,5))


def cross2(a,b):
    return a[...,0]*b[...,1]-a[...,1]*b[...,0]


def key(p):
    return tuple(round(float(v),5) for v in p)


def centerline(mesh,sides):
    p=np.asarray(mesh['positions'])
    count=len(p)-sides*6
    sections=count//(sides*4)
    sets=[{key(q) for q in p[i*sides*4:(i+1)*sides*4]} for i in range(sections)]
    ring=[{key(q) for q in p[count:count+sides*3] if key(q)!=key(p[count])}]
    ring.extend(sets[i-1]&sets[i] for i in range(1,sections))
    ring.append({key(q) for q in p[count+sides*3:] if key(q)!=key(p[count+sides*3])})
    centers=np.asarray([np.asarray(list(r)).mean(axis=0) for r in ring])
    distance=np.r_[0,np.cumsum(np.linalg.norm(np.diff(centers,axis=0),axis=1))]
    return centers,distance


def material_kind(part):
    raw=part['path'].split('/')[-1]
    if 'Gasket' in raw or 'Gland' in raw or raw=='RubberSeat' or 'ContactRunner' in raw:
        return 'rubber'
    return MATERIAL[part['material']]


def extend_layers(layers,owner,pixels=6):
    owner=owner.copy()
    for step in range(pixels):
        old=owner.copy()
        for dy,dx in [(0,-1),(0,1),(-1,0),(1,0),(-1,-1),(-1,1),(1,-1),(1,1)]:
            sy=slice(max(0,-dy),min(512,512-dy));sx=slice(max(0,-dx),min(512,512-dx))
            ty=slice(max(0,dy),min(512,512+dy));tx=slice(max(0,dx),min(512,512+dx))
            valid=(owner[ty,tx]==0)&(old[sy,sx]>0)
            owner[ty,tx][valid]=old[sy,sx][valid]
            for layer in layers:
                layer[ty,tx][valid]=layer[sy,sx][valid]
    return owner


def composite(layers):
    result=Image.new('RGBA',(512,512),(20,25,23,255))
    for layer in layers:
        result.alpha_composite(Image.fromarray(layer,'RGBA'))
    return result


def openraster(file,layers,merged):
    node=ET.Element('image',{'w':'512','h':'512','name':file.stem,'version':'0.0.3'})
    stack=ET.SubElement(node,'stack')
    with zipfile.ZipFile(file,'w') as z:
        z.writestr('mimetype','image/openraster',compress_type=zipfile.ZIP_STORED)
        for i,(title,layer) in enumerate(reversed(list(zip(['01_Base','02_Shading','03_Wear'],layers)))):
            path=f'data/layer{i}.png'
            im=Image.fromarray(layer,'RGBA');data=BytesIO();im.save(data,format='PNG')
            z.writestr(path,data.getvalue(),compress_type=zipfile.ZIP_DEFLATED)
            ET.SubElement(stack,'layer',{'name':title,'src':path,'x':'0','y':'0','opacity':'1.0','visibility':'visible','composite-op':'svg:src-over'})
        z.writestr('stack.xml',ET.tostring(node,encoding='utf-8'))
        data=BytesIO();merged.save(data,format='PNG');z.writestr('mergedimage.png',data.getvalue(),compress_type=zipfile.ZIP_DEFLATED)


def closest_radial(points,centers):
    best=np.full(len(points),np.inf);radial=np.zeros_like(points)
    for a,b in zip(centers[:-1],centers[1:]):
        edge=b-a;t=np.clip(((points-a)*edge).sum(axis=1)/np.dot(edge,edge),0,1)
        offset=points-a-t[:,None]*edge
        dist=(offset*offset).sum(axis=1);use=dist<best
        radial[use]=offset[use];best[use]=dist[use]
    return radial/np.maximum(1e-8,np.linalg.norm(radial,axis=1))[:,None]


def shift(mask,dy,dx=0):
    result=np.zeros_like(mask)
    sy=slice(max(0,-dy),min(512,512-dy));sx=slice(max(0,-dx),min(512,512-dx))
    ty=slice(max(0,dy),min(512,512+dy));tx=slice(max(0,dx),min(512,512+dx))
    result[ty,tx]=mask[sy,sx]
    return result


def family_for(part):
    raw=part['path'].split('/')[-1]
    if raw=='MainCurvedConduit':return 'redrubber'
    if raw=='AuxiliaryCurvedConduit':return 'auxrubber'
    if raw in ('HeadCrownPlate','BankServiceCover'):return 'brown'
    return material_kind(part)


def palette_image():
    image=Image.new('RGB',(770,24+len(RAMPS)*76),'#19221e');draw=ImageDraw.Draw(image)
    font=ImageFont.truetype('C:/Windows/Fonts/malgun.ttf',18)
    for row,(name,colors) in enumerate(RAMPS.items()):
        draw.text((16,16+row*76),name,font=font,fill='#e2dfc6')
        for col,color in enumerate(colors):
            x=145+col*145;y=10+row*76
            draw.rectangle((x,y,x+130,y+42),fill=color);draw.text((x,y+44),color,font=font,fill='#b9c2b5')
    image.save(ROOT/'Source/Palette.png')


def paint_material(island,part,atlas,base,shading,details,owner,source,curves):
    family=family_for(part);ramp=np.asarray([rgb(c) for c in RAMPS[family]],dtype=np.uint8)
    points=np.array([[p['x'],512-p['y']] for p in island['points']]);positions=np.array([[p['x'],p['y'],p['z']] for p in island['positions']])
    raw=part['path'].split('/')[-1];curve=raw in curves
    mask=np.zeros((512,512),dtype=bool)
    for k in range(0,len(island['triangles']),3):
        ids=island['triangles'][k:k+3];p=points[ids];xyz=positions[ids]
        xmin=max(0,int(np.floor(p[:,0].min())));xmax=min(511,int(np.floor(p[:,0].max())))
        ymin=max(0,int(np.floor(p[:,1].min())));ymax=min(511,int(np.floor(p[:,1].max())))
        xx,yy=np.meshgrid(np.arange(xmin,xmax+1),np.arange(ymin,ymax+1));sample=np.stack((xx+.5,yy+.5),axis=-1)
        det=cross2(p[1]-p[0],p[2]-p[0]);u=cross2(sample-p[0],p[2]-p[0])/det;v=cross2(p[1]-p[0],sample-p[0])/det
        good=(u>=-1e-5)&(v>=-1e-5)&(u+v<=1+1e-5)
        if not good.any():continue
        x=xx[good];y=yy[good];point=xyz[0]+u[good,None]*(xyz[1]-xyz[0])+v[good,None]*(xyz[2]-xyz[0])
        level=np.ones(len(x),dtype=int)
        if family=='screen':level[:]=0
        elif curve:
            # 약한 성형 고무 톤만 작성한다. 넓은 방향성 광 명암은 실제 조명에서 온다.
            radial=closest_radial(point,curves[raw][0]);facing=radial@np.array([-.25,.45,-.85])
            level=np.where(facing<-.65,0,1)
            if raw=='MainCurvedConduit':level=np.where(facing>.78,2,level)
        elif family=='rubber':level[:]=0 if 'Gasket' in raw else 1
        elif raw in ('Washer','HexHead'):level[:]=1
        elif raw in ('FacetedLowerPressureCase','FoldedBankHeadCase','LowerRemovableCover') and island['area']*island['density']**2>900:
            sx=((x-island['x'])//4*4)%64;sy=((y-(512-island['y']-island['height']))//4*4)%64
            field=source[0 if family=='green' else 1][sy,sx]
            # 미세한 도장 빛바램만 남기고 법선 방향의 광 명암은 굽지 않는다.
            level[field>.88]=2
        base[y,x,:3]=ramp[0 if family=='screen' else 1];base[y,x,3]=255
        shading[y,x,:3]=ramp[level];shading[y,x,3]=255
        owner[y,x]=island['id'];mask[y,x]=True
    if not mask.any() or curve or family in ('screen','rubber'):return
    if island['area']*island['density']**2<300:return
    # 전체를 밝게 둘러 그리지 않고 접촉 레일과 고정부 일부만 마모시킨다.
    wear_parts=('LowerProtectionRail','RaisedTrayLip','BankProtectiveBezel','SharedLifetimeProtectiveBezel','ConnectorFacePlate','HeadCrownPlate','BentLowerBand_-1')
    if raw not in wear_parts:return
    yy,xx=np.indices(mask.shape);x0=island['x'];y0=512-island['y']-island['height'];w=island['width'];h=island['height']
    edge=mask&~shift(mask,2)
    # 서로 길이가 다른 접촉부 2곳만 중간톤으로 드러낸다. 밝은 프레임 전체 윤곽은 없다.
    contact=edge&(((xx>=x0+3)&(xx<x0+11))|((xx>=x0+w-22)&(xx<x0+w-17)))
    details[contact,:3]=ramp[2];details[contact,3]=255
    if w>=24 and h>=9:
        chip=Image.new('L',(512,512),0);d=ImageDraw.Draw(chip)
        x=x0+int(w*.66);y=y0+2
        d.polygon([(x,y),(x+4,y),(x+4,y+1),(x+7,y+1),(x+7,y+3),(x+2,y+3),(x+2,y+2),(x,y+2)],fill=255)
        worn=(np.asarray(chip)>0)&mask
        details[worn,:3]=ramp[2];details[worn,3]=255
        # 고정 볼트 근처의 아주 짧은 금속 노출과 사용 중 쌓인 가장자리 때.
        exposed=contact&(xx<x0+6)
        details[exposed,:3]=ramp[3];details[exposed,3]=255
        grime=mask&(yy>=y0+h-3)&(xx>x0+w*.25)&(xx<x0+w*.42)
        details[grime,:3]=ramp[0];details[grime,3]=255


def source_maps():
    result=[];image=Image.open(ROOT/'Source/MaterialSource_ImageGen.png').convert('RGB')
    for row,col in [(0,0),(0,1),(1,0),(1,1)]:
        w=image.width//2;h=image.height//2
        tile=image.crop((col*w+w*.1,row*h+h*.1,(col+1)*w-w*.1,(row+1)*h-h*.1)).resize((12,12),Image.Resampling.NEAREST)
        lum=np.asarray(tile.resize((64,64),Image.Resampling.NEAREST)).mean(axis=2);lo,hi=np.percentile(lum,[8,92])
        result.append((lum-lo)/max(1,hi-lo))
    return result


def physical_contact(island,raw,ramp,details):
    """UV belt의 시작점 대신 실제 표면 축을 기준으로 보이는 면에 큰 색 덩어리를 둔다."""
    targets=('HeadCrownPlate','ShoulderShelf','LowerProtectionRail','BankProtectiveBezel','SharedLifetimeProtectiveBezel','AngularConnectorCase','BentLowerBand_-1')
    if raw not in targets:return
    points=np.array([[p['x'],512-p['y']] for p in island['points']]);positions=np.array([[p['x'],p['y'],p['z']] for p in island['positions']])
    for k in range(0,len(island['triangles']),3):
        ids=island['triangles'][k:k+3];uv=points[ids];xyz=positions[ids]
        xmin=max(0,int(np.floor(uv[:,0].min())));xmax=min(511,int(np.floor(uv[:,0].max())))
        ymin=max(0,int(np.floor(uv[:,1].min())));ymax=min(511,int(np.floor(uv[:,1].max())))
        xx,yy=np.meshgrid(np.arange(xmin,xmax+1),np.arange(ymin,ymax+1));sample=np.stack((xx+.5,yy+.5),axis=-1)
        det=cross2(uv[1]-uv[0],uv[2]-uv[0]);a=cross2(sample-uv[0],uv[2]-uv[0])/det;b=cross2(uv[1]-uv[0],sample-uv[0])/det
        good=(a>=-1e-5)&(b>=-1e-5)&(a+b<=1+1e-5)
        if not good.any():continue
        x=xx[good];y=yy[good];p=xyz[0]+a[good,None]*(xyz[1]-xyz[0])+b[good,None]*(xyz[2]-xyz[0])
        normal=np.cross(xyz[1]-xyz[0],xyz[2]-xyz[0]);normal/=np.linalg.norm(normal)
        top=normal[1]>.6;front=abs(normal[2])>.7
        axes=(0,2) if top else (0,1)
        cell=4/island['density'];lo=positions[:,axes].min(axis=0);hi=positions[:,axes].max(axis=0)
        U=np.floor((p[:,axes[0]]-lo[0])/cell);V=np.floor((p[:,axes[1]]-lo[1])/cell)
        W=max(1,int(np.ceil((hi[0]-lo[0])/cell)));H=max(1,int(np.ceil((hi[1]-lo[1])/cell)))
        dark=np.zeros(len(x),bool);light=np.zeros(len(x),bool)
        if raw in ('HeadCrownPlate','ShoulderShelf') and top:
            dark=((U>=3)&(U<9)&(V>=1)&(V<3))|((U>=5)&(U<12)&(V>=3)&(V<5))|((U>=7)&(U<10)&(V>=5)&(V<6))
            light=((U>=W-10)&(U<W-5)&(V>=1)&(V<3))|((U>=W-8)&(U<W-3)&(V>=3)&(V<5))
        elif raw=='LowerProtectionRail' and front:
            dark=((U>=W//3)&(U<W//3+5)&(V<1))|((U>=W//3+3)&(U<W//3+8)&(V>=1)&(V<3))
            light=((U>=W-9)&(U<W-5)&(V>=1))|((U>=W-7)&(U<W-3)&(V<1))
        elif raw in ('BankProtectiveBezel','SharedLifetimeProtectiveBezel') and front:
            light=((U>=5)&(U<10)&(V>=H-2))|((U>=9)&(U<13)&(V>=H-1))
            dark=((U>=W-12)&(U<W-7)&(V<1))|((U>=W-9)&(U<W-5)&(V<2))
        elif raw in ('AngularConnectorCase','BentLowerBand_-1') and front:
            dark=((U>=1)&(U<3)&(V>=2)&(V<5))|((U>=2)&(U<5)&(V>=4)&(V<7))
        details[y[dark],x[dark],:3]=ramp[0];details[y[dark],x[dark],3]=255
        details[y[light],x[light],:3]=ramp[2];details[y[light],x[light],3]=255


def draw_island(island,part,atlas,base,shading,details,owner,source,curves):
    paint_material(island,part,atlas,base,shading,details,owner,source,curves)
    family=family_for(part);raw=part['path'].split('/')[-1]
    if family in ('rubber','redrubber','auxrubber','screen','steel'):return
    mask=owner==island['id'];ramp=np.asarray([rgb(c) for c in RAMPS[family]],dtype=np.uint8)
    if not mask.any():return
    # 넓은 면의 미세 도장 흔적은 지우고 큰 논리 픽셀 색면으로 다시 구성한다.
    shading[mask,:3]=ramp[1];shading[mask,3]=255;details[mask]=0
    x0=island['x'];y0=512-island['y']-island['height'];w=island['width'];h=island['height'];g=4
    if island['area']*island['density']**2<180:return
    sheet=Image.new('L',(512,512),0);d=ImageDraw.Draw(sheet)
    def polygon(points,level):
        d.polygon([(x0+int(x)*g,y0+int(y)*g) for x,y in points],fill=level)
    def rect(a,b,c,e,level):d.rectangle((x0+int(a)*g,y0+int(b)*g,x0+int(c)*g-1,y0+int(e)*g-1),fill=level)
    W=max(1,w//g);H=max(1,h//g)
    if raw in ('HeadCrownPlate','ShoulderShelf','FacetedLowerPressureCase','FoldedBankHeadCase','AngularEndReinforcement_-1','BentLowerBand_-1') and w>=20 and h>=12:
        # 평면을 작은 얼룩으로 채우지 않고, 접촉 가장자리에 연결된 큰 계단 덩어리 두 개만 둔다.
        polygon([(1,1),(5,1),(5,2),(7,2),(7,3),(4,3),(4,4),(2,4),(2,2),(1,2)],2)
        if W>14:
            polygon([(W-8,H-3),(W-5,H-3),(W-5,H-2),(W-2,H-2),(W-2,H-1),(W-7,H-1),(W-7,H-2),(W-8,H-2)],3)
        if W>20 and H>4:rect(W//2,H-2,W//2+5,H-1,1)
    if raw in ('BankProtectiveBezel','SharedLifetimeProtectiveBezel','LowerProtectionRail','RaisedTrayLip','ConnectorFacePlate','RetainingClamp_-1_-1'):
        if H<=3:
            rect(1,0,min(5,W-1),1,3)
            if W>15:rect(W-10,max(0,H-1),W-5,H,1)
        else:
            polygon([(1,1),(5,1),(5,2),(7,2),(7,3),(3,3),(3,2),(1,2)],3)
            rect(1,max(0,H-2),min(W-1,8),H-1,1)
            if W>18:rect(W-8,H-2,W-3,H-1,3)
    if raw=='LowerProtectionRail' and W>20:rect(W//2-2,max(0,H-1),W//2+3,H,3)
    pattern=np.asarray(sheet);selected=(pattern>0)&mask
    # 1=어두운 재질 경계, 2/3=도장 마모. 프레임 전체 둘레를 밝히지 않는다.
    for code,level in [(1,0),(2,2),(3,2)]:
        use=(pattern==code)&selected;details[use,:3]=ramp[level];details[use,3]=255
    physical_contact(island,raw,ramp,details)


def main():
    parser=argparse.ArgumentParser();parser.add_argument('--output',type=Path,default=ROOT);args=parser.parse_args()
    assert args.output==ROOT, '출력은 최종 Counter 경로로 고정합니다.'
    manifest=json.loads((UV/'UVManifest.json').read_text(encoding='utf-8'))
    geometry=json.loads((ROOT/'Source/geometry.json').read_text(encoding='utf-8'))
    curves={}
    for mesh in geometry:
        raw=mesh['path'].split('/')[-1]
        if 'CurvedConduit' in raw or raw=='07_UnterminatedCableRoute':
            curves[raw]=centerline(mesh,10 if raw.startswith('07_') else 12)
    for folder in ['Textures','Layers','Source/Ownership']:
        (ROOT/folder).mkdir(parents=True,exist_ok=True)
    parts={p['path']:p for p in manifest['parts']};sources=source_maps();records=[]
    for ai,atlas in enumerate(manifest['atlases']):
        layers=[np.zeros((512,512,4),dtype=np.uint8) for _ in range(3)]
        owner=np.zeros((512,512),dtype=np.uint16)
        for island in manifest['islands']:
            if island['atlas']==ai:
                draw_island(island,parts[island['part']],atlas,*layers,owner,sources,curves)
        original=owner.copy()
        owner=extend_layers(layers,owner)
        final=composite(layers)
        file=ROOT/'Textures'/f'{atlas["name"]}_PIXEL.png'
        final.save(file)
        for title,layer in zip(['01_Base','02_Shading','03_Wear'],layers):
            Image.fromarray(layer,'RGBA').save(ROOT/'Layers'/f'{atlas["name"]}_{title}.png')
        openraster(ROOT/'Layers'/f'{atlas["name"]}.ora',layers,final)
        Image.fromarray(owner).save(ROOT/'Source/Ownership'/f'{atlas["name"]}_OWNERSHIP.png')
        records.append({'atlas':atlas['name'],'texture':str(file.relative_to(PROJECT)).replace('\\','/'),'colors':len(final.getcolors(512*512)),
                        'coveredPixels':int((original>0).sum()),'paddedPixels':int(((owner>0)&(original==0)).sum()),'sha256':hashlib.sha256(file.read_bytes()).hexdigest()})
    palette={'ramps':RAMPS,'materialRamps':MATERIAL,'curves':{name:{'centers':c.tolist(),'distances':d.tolist()} for name,(c,d) in curves.items()},'atlases':records}
    (ROOT/'Source/palette.json').write_text(json.dumps(palette,indent=2),encoding='utf-8')
    palette['logicalPixelTexels']=4
    palette['partMaterials']=[{'path':p['path'],'family':family_for(p)} for p in manifest['parts']]
    (ROOT/'Source/palette.json').write_text(json.dumps(palette,ensure_ascii=False,indent=2),encoding='utf-8')
    palette_image()
    print(json.dumps(records,indent=2))


if __name__ == '__main__':
    main()
