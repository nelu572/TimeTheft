"""Galmuri7 원본 비트맵 라벨과 9×14 픽셀 디지털 글리프를 TMP 입력으로 만든다.

숫자 값은 텍스처에 굽지 않는다. Galmuri 배포 라이선스는 Source/Galmuri에 보존한다.
"""
import json
from pathlib import Path
from PIL import Image, ImageDraw

PROJECT = Path(__file__).resolve().parents[2]
ROOT = PROJECT / 'Assets/TimeTheft/Counter'
CHARS = ' ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789:-'



def bdf(path,name,point):
    text=path.read_text(encoding='utf-8');lines=text.splitlines();glyphs={};props={}
    for line in lines[:30]:
        if line.startswith(('FONT_ASCENT ','FONT_DESCENT ','CAP_HEIGHT ')):
            key,value=line.split();props[key]=int(value)
    for chunk in text.split('STARTCHAR ')[1:]:
        block=chunk.split('ENDCHAR')[0].splitlines();entry={}
        for i,line in enumerate(block):
            if line.startswith('ENCODING '):entry['unicode']=int(line.split()[1])
            elif line.startswith('DWIDTH '):entry['advance']=int(line.split()[1])
            elif line.startswith('BBX '):entry['box']=list(map(int,line.split()[1:]))
            elif line=='BITMAP':entry['rows']=block[i+1:]
        code=entry.get('unicode',-1)
        if code not in map(ord,CHARS):continue
        w,h,xoff,yoff=entry['box'];im=Image.new('RGBA',(max(w,1),max(h,1)),(255,255,255,0))
        for y,row in enumerate(entry['rows'][:h]):
            value=int(row,16);bitwidth=len(row)*4
            for x in range(w):
                if value&(1<<(bitwidth-1-x)):im.putpixel((x,y),(255,255,255,255))
        glyphs[code]={'image':im,'width':w,'height':h,'bearingX':xoff,'bearingY':yoff+h,'advance':entry['advance']}
    missing=set(map(ord,CHARS))-set(glyphs);assert not missing,missing
    return {'name':name,'pointSize':point,'ascent':props['FONT_ASCENT'],'descent':-props['FONT_DESCENT'],'cap':props['CAP_HEIGHT'],'glyphs':glyphs}


def segmented():
    rectangles={'a':(2,0,6,1),'b':(7,1,8,6),'c':(7,7,8,12),'d':(2,12,6,13),'e':(0,7,1,12),'f':(0,1,1,6),'g':(2,6,6,7)}
    active={'0':'ab cdef'.replace(' ',''),'1':'bc','2':'abged','3':'abgcd','4':'fgbc','5':'afgcd','6':'afgecd','7':'abc','8':'abcdefg','9':'abfgcd'}
    glyphs={}
    for char in '0123456789: -':
        width=3 if char==':' else 9
        im=Image.new('RGBA',(width,14),(255,255,255,0));d=ImageDraw.Draw(im)
        if char in active:
            for segment in active[char]:d.rectangle(rectangles[segment],fill=(255,255,255,255))
        elif char==':':
            d.rectangle((0,4,1,5),fill='white');d.rectangle((0,9,1,10),fill='white')
        elif char=='-':d.rectangle(rectangles['g'],fill='white')
        glyphs[ord(char)]={'image':im,'width':0 if char==' ' else width,'height':0 if char==' ' else 14,'bearingX':0,'bearingY':14,'advance':5 if char==':' else 11}
    return {'name':'CounterPixelSegments','pointSize':16,'ascent':14,'descent':-2,'cap':14,'glyphs':glyphs}


def save_font(spec):
    image=Image.new('RGBA',(128,128),(255,255,255,0));glyphs=[]
    for i,(code,source) in enumerate(sorted(spec['glyphs'].items())):
        x=2+(i%8)*16;y=2+(i//8)*20
        assert y+source['image'].height<128
        image.alpha_composite(source['image'],(x,y))
        glyphs.append({'unicode':code,'x':x,'y':128-y-source['height'],'width':source['width'],'height':source['height'],
                       'bearingX':source['bearingX'],'bearingY':source['bearingY'],'advance':source['advance']})
    image.save(ROOT/'Fonts'/f'{spec["name"]}_Glyphs.png')
    data={key:value for key,value in spec.items() if key!='glyphs'};data['glyphs']=glyphs
    (ROOT/'Fonts'/f'{spec["name"]}.json').write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')


def main():
    (ROOT/'Fonts').mkdir(exist_ok=True)
    for spec in (bdf(ROOT/'Source/Galmuri/Galmuri7.bdf', 'Galmuri7Native', 8), segmented()):
        save_font(spec)



if __name__ == '__main__':
    main()
