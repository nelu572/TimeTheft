"""최종 계수기 PNG/UV 이음새, 팔레트와 편집 레이어를 독립 검사한다.

작성기를 import하지 않고 동일 3D 경계와 스칼라 기준식으로 검사한다.
UV/메시/Unity 상태 및 입력 자산은 변경하지 않으며 --output JSON만 저장한다.
"""
import argparse
import collections
import copy
import hashlib
import json
import math
from pathlib import Path
import re
import struct
import zlib
from io import BytesIO
import zipfile
import numpy as np
from PIL import Image

PROJECT = Path(__file__).resolve().parents[2]
ROOT = PROJECT / 'Assets/TimeTheft/Counter'
TUBES = ('MainCurvedConduit', 'AuxiliaryCurvedConduit', '07_UnterminatedCableRoute')



def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def vec(value, dimensions=3):
    return tuple(value[key] for key in 'xyz'[:dimensions])


def point_key(value):
    return tuple(round(v * 100000) for v in value)


def lerp(a, b, t):
    return tuple(x + (y - x) * t for x, y in zip(a, b))


def distance(a, b):
    return math.sqrt(sum((x - y) ** 2 for x, y in zip(a, b)))


def subtract(a, b):
    return tuple(x - y for x, y in zip(a, b))


def dot(a, b):
    return sum(x * y for x, y in zip(a, b))


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def cross2(a, b):
    return a[0] * b[1] - a[1] * b[0]


def read_png(path):
    """8-bit RGB/RGBA PNG를 표준 라이브러리만으로 읽는다. 원본 행은 위→아래."""
    data = path.read_bytes()
    if data[:8] != b'\x89PNG\r\n\x1a\n':
        raise ValueError(f'Not PNG: {path}')
    offset, packed = 8, bytearray()
    width = height = channels = None
    while offset < len(data):
        length = struct.unpack('>I', data[offset:offset + 4])[0]
        tag = data[offset + 4:offset + 8]
        payload = data[offset + 8:offset + 8 + length]
        checksum = struct.unpack('>I', data[offset + 8 + length:offset + 12 + length])[0]
        if zlib.crc32(tag + payload) & 0xffffffff != checksum:
            raise ValueError(f'PNG CRC mismatch: {path} / {tag!r}')
        if tag == b'IHDR':
            width, height, depth, color, compression, filtering, interlace = struct.unpack('>IIBBBBB', payload)
            if depth != 8 or color not in (2, 6) or compression or filtering or interlace:
                raise ValueError(f'Unsupported PNG format: {path} / depth={depth}, color={color}, interlace={interlace}')
            channels = 4 if color == 6 else 3
        elif tag == b'IDAT':
            packed.extend(payload)
        offset += length + 12
    raw = zlib.decompress(packed)
    stride = width * channels
    if len(raw) != (stride + 1) * height:
        raise ValueError(f'PNG scanline size mismatch: {path}')
    rows, previous, offset = [], bytearray(stride), 0
    for _ in range(height):
        mode = raw[offset]
        row = bytearray(raw[offset + 1:offset + 1 + stride])
        offset += stride + 1
        for i in range(stride):
            a = row[i - channels] if i >= channels else 0
            b = previous[i]
            c = previous[i - channels] if i >= channels else 0
            if mode == 0:
                prediction = 0
            elif mode == 1:
                prediction = a
            elif mode == 2:
                prediction = b
            elif mode == 3:
                prediction = (a + b) // 2
            elif mode == 4:
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                prediction = a if pa <= pb and pa <= pc else b if pb <= pc else c
            else:
                raise ValueError(f'Invalid PNG filter: {mode}')
            row[i] = (row[i] + prediction) & 255
        rows.append(row)
        previous = row
    return {'width': width, 'height': height, 'channels': channels, 'rows': rows,
            'sha256': hashlib.sha256(data).hexdigest()}


def sample_png(image, uv):
    # Manifest coordinates are texels from the bottom-left; PNG rows start at top-left.
    x, y = math.floor(uv[0]), math.floor(uv[1])
    if not (0 <= x < image['width'] and 0 <= y < image['height']):
        raise ValueError(f'UV sample outside PNG: {uv}')
    start = x * image['channels']
    color = tuple(image['rows'][image['height'] - 1 - y][start:start + image['channels']])
    return color if len(color) == 4 else color + (255,)


def boundary_edges(island):
    result = []
    positions = [vec(p) for p in island['positions']]
    points = [vec(p, 2) for p in island['points']]
    triangles = [island['triangles'][i:i + 3] for i in range(0, len(island['triangles']), 3)]
    for i in range(0, len(island['boundary']), 2):
        a, b = island['boundary'][i:i + 2]
        triangle = next(t for t in triangles if a in t and b in t)
        third = next(p for p in triangle if p not in (a, b))
        if point_key(positions[a]) > point_key(positions[b]):
            a, b = b, a
        uv0, uv1 = points[a], points[b]
        dx, dy = uv1[0] - uv0[0], uv1[1] - uv0[1]
        length = math.hypot(dx, dy)
        if length < 1e-8:
            raise ValueError(f'Degenerate boundary UV: island {island["id"]}')
        inward = (-dy / length, dx / length)
        if sum((points[third][j] - uv0[j]) * inward[j] for j in (0, 1)) < 0:
            inward = tuple(-v for v in inward)
        result.append({'island': island['id'], 'face': island['face'], 'mapping': island['mapping'],
                       'atlas': island['atlas'], 'positions': [positions[a], positions[b]],
                       'uv': [uv0, uv1], 'thirdUv': points[third], 'inward': inward, 'uvLength': length})
    return result


def classify(a, b):
    if a['mapping'] == 'planar' or b['mapping'] == 'planar':
        return 'cap_join'
    gore_a = re.search(r'GORE\s+(\d+)', a['face'])
    gore_b = re.search(r'GORE\s+(\d+)', b['face'])
    return 'length_chunk' if gore_a.group(1) == gore_b.group(1) else 'circumference'


def prepare(manifest):
    result = []
    for suffix in TUBES:
        islands = [i for i in manifest['islands'] if i['part'].endswith(suffix)]
        if not islands:
            raise ValueError(f'Missing tube in manifest: {suffix}')
        grouped = collections.defaultdict(list)
        for island in islands:
            for edge in boundary_edges(island):
                key = tuple(point_key(p) for p in edge['positions'])
                grouped[key].append(edge)
        unmatched = [{'edge': key, 'occurrences': len(edges)} for key, edges in grouped.items() if len(edges) != 2]
        pairs = []
        for edges in grouped.values():
            if len(edges) != 2:
                continue
            a, b = edges
            error = max(distance(a['positions'][j], b['positions'][j]) for j in (0, 1))
            pairs.append({'kind': classify(a, b), 'positionError': error, 'sides': [a, b]})
        parts = [p for p in manifest['parts'] if p['path'] == islands[0]['part']]
        result.append({'part': islands[0]['part'], 'name': suffix, 'islands': len(islands),
                       'sharedPaths': parts[0]['sharedPaths'], 'unmatchedEdges': unmatched,
                       'maxPositionError': max((p['positionError'] for p in pairs), default=0),
                       'pairCounts': dict(collections.Counter(p['kind'] for p in pairs)), 'pairs': pairs})
    return result


def inset_uv(edge, t, requested):
    """UV 꼭짓점 근처에서도 해당 경계 삼각형 밖으로 나가지 않는 안쪽 표본."""
    a, b = edge['uv']
    c = edge['thirdUv']
    ab, ac = subtract(b, a), subtract(c, a)
    determinant = cross2(ab, ac)
    db = cross2(edge['inward'], ac) / determinant
    dc = cross2(ab, edge['inward']) / determinant
    da = -db - dc
    limits = [weight / -rate for weight, rate in ((1 - t, da), (t, db), (0, dc)) if rate < -1e-10]
    amount = min(requested, min(limits) * .45) if limits else requested
    uv = lerp(a, b, t)
    return tuple(uv[j] + edge['inward'][j] * amount for j in (0, 1))


def reference_pixels(manifest, palette, images, tubes):
    ramp = [tuple(int(c[i:i + 2], 16) for i in (1, 3, 5)) for c in palette['ramps']['rubber']]
    reports = []
    pixel_data = {}
    for suffix in tubes:
        mismatches, tested = [], 0
        for island in (i for i in manifest['islands'] if i['part'].endswith(suffix)):
            q = [vec(v, 2) for v in island['points']]
            p = [vec(v) for v in island['positions']]
            pixels = {}
            for start in range(0, len(island['triangles']), 3):
                ids = island['triangles'][start:start + 3]
                uv, xyz = [q[i] for i in ids], [p[i] for i in ids]
                ab, ac = subtract(uv[1], uv[0]), subtract(uv[2], uv[0])
                determinant = cross2(ab, ac)
                n = cross(subtract(xyz[1], xyz[0]), subtract(xyz[2], xyz[0]))
                length = math.sqrt(dot(n, n))
                n = tuple(v / length for v in n)
                xmin, xmax = math.floor(min(v[0] for v in uv)), math.floor(max(v[0] for v in uv))
                ymin, ymax = math.floor(min(v[1] for v in uv)), math.floor(max(v[1] for v in uv))
                for y in range(ymin, ymax + 1):
                    for x in range(xmin, xmax + 1):
                        delta = subtract((x + .5, y + .5), uv[0])
                        u, v = cross2(delta, ac) / determinant, cross2(ab, delta) / determinant
                        if u < -1e-5 or v < -1e-5 or u + v > 1 + 1e-5:
                            continue
                        point = tuple(xyz[0][j] + u * (xyz[1][j] - xyz[0][j]) + v * (xyz[2][j] - xyz[0][j]) for j in range(3))
                        level, along = tube_reference(point, n, suffix, palette['curves'][suffix])
                        pixels[(x, y)] = {'point': point, 'normal': n, 'level': level, 'distance': along, 'expectedRgb': ramp[level]}
            for coord, data in pixels.items():
                actual = sample_png(images[island['atlas']], (coord[0] + .5, coord[1] + .5))
                tested += 1
                if actual[:3] != data['expectedRgb']:
                    mismatches.append({'island': island['id'], 'pixel': coord, 'rgba': actual, 'expected': data['expectedRgb']})
            pixel_data[island['id']] = pixels
        reports.append({'name': suffix, 'surfacePixelChecks': tested, 'referenceMismatchCount': len(mismatches),
                        'firstMismatches': mismatches[:12]})
    return ramp, pixel_data, reports


def padding_coverage(manifest, layer_directory):
    """합성 배경의 불투명도 대신 Base 레이어 alpha로 실제 6px 색 확장을 검사."""
    result = []
    for ai, atlas in enumerate(manifest['atlases']):
        path = layer_directory / (atlas['name'] + '_01_Base.png')
        if not path.exists():
            result.append({'atlas': atlas['name'], 'status': 'base_layer_missing'})
            continue
        image = read_png(path)
        width, height = atlas['width'], atlas['height']
        original = set()
        for island in (i for i in manifest['islands'] if i['atlas'] == ai):
            points = [vec(v, 2) for v in island['points']]
            for start in range(0, len(island['triangles']), 3):
                uv = [points[i] for i in island['triangles'][start:start + 3]]
                ab, ac = subtract(uv[1], uv[0]), subtract(uv[2], uv[0])
                determinant = cross2(ab, ac)
                for y in range(math.floor(min(v[1] for v in uv)), math.floor(max(v[1] for v in uv)) + 1):
                    for x in range(math.floor(min(v[0] for v in uv)), math.floor(max(v[0] for v in uv)) + 1):
                        delta = subtract((x + .5, y + .5), uv[0])
                        u, v = cross2(delta, ac) / determinant, cross2(ab, delta) / determinant
                        if u >= -1e-5 and v >= -1e-5 and u + v <= 1 + 1e-5:
                            original.add((x, y))
        covered, frontier = set(original), set(original)
        for _ in range(manifest['padding']):
            following = set()
            for x, y in frontier:
                for dx, dy in ((-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1)):
                    p = x + dx, y + dy
                    if 0 <= p[0] < width and 0 <= p[1] < height and p not in covered:
                        following.add(p)
            covered.update(following)
            frontier = following
        holes = [p for p in covered if sample_png(image, (p[0] + .5, p[1] + .5))[3] != 255]
        result.append({'atlas': atlas['name'], 'originalSurfacePixels': len(original),
                       'requiredSurfaceAndPaddingPixels': len(covered), 'paddingRadiusChebyshev': manifest['padding'],
                       'missingOpaqueBasePixels': len(holes), 'firstMissing': holes[:12],
                       'path': str(path), 'sha256': image['sha256']})
    return result


def tube_reference(point, normal, name, curve):
    best = float('inf')
    radial = (0, 0, 0)
    along = 0
    for index, (a, b) in enumerate(zip(curve['centers'][:-1], curve['centers'][1:])):
        ab = subtract(b, a)
        t = min(1, max(0, dot(subtract(point, a), ab) / dot(ab, ab)))
        offset = subtract(point, lerp(a, b, t))
        squared = dot(offset, offset)
        if squared < best:
            best, radial = squared, offset
            along = curve['distances'][index] + t * (curve['distances'][index + 1] - curve['distances'][index])
    size = max(1e-8, math.sqrt(dot(radial, radial)))
    facing = dot(radial, (-.25, .45, -.85)) / size
    if name == 'MainCurvedConduit':
        level = 2 if facing > .78 else 0 if facing < -.65 else 1
    else:
        level = 0 if facing < -.65 else 1
    return level, along


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    root = ROOT
    manifest_path = ROOT / 'Source/UV/UVManifest.json'
    manifest = json.loads(manifest_path.read_text(encoding='utf-8-sig'))
    palette = json.loads((root / 'Source/palette.json').read_text(encoding='utf-8-sig'))
    parts = prepare(manifest)
    images = [read_png(root / 'Textures' / (a['name'] + '_PIXEL.png')) for a in manifest['atlases']]
    for atlas, image in zip(manifest['atlases'], images):
        if (image['width'], image['height']) != (atlas['width'], atlas['height']):
            raise ValueError('Atlas size mismatch: ' + atlas['name'])
    pixels = {}; reference = []; ramps = {}
    for suffix, family in [('MainCurvedConduit','redrubber'),('AuxiliaryCurvedConduit','auxrubber'),('07_UnterminatedCableRoute','rubber')]:
        local = copy.deepcopy(palette); local['ramps']['rubber'] = palette['ramps'][family]
        ramp, pixel_part, reports = reference_pixels(manifest, local, images, (suffix,))
        pixels.update(pixel_part); reference.extend(reports); ramps[suffix] = ramp
    seams = []
    failures = []
    for part in parts:
        levels = {v:k for k,v in enumerate(ramps[part['name']])}
        bins = collections.defaultdict(list)
        unknown, transparent, reviews = 0, 0, []
        for pair in part['pairs']:
            a, b = pair['sides']
            count = max(1, math.ceil(max(a['uvLength'], b['uvLength']) / .25))
            for i in range(count):
                t = (i + .5) / count
                uvs = [inset_uv(edge, t, .25) for edge in (a, b)]
                colors = [sample_png(images[edge['atlas']], uv) for edge, uv in zip((a, b), uvs)]
                transparent += any(c[3] != 255 for c in colors)
                unknown += any(c[:3] not in levels for c in colors)
                step = abs(levels.get(colors[0][:3], 0) - levels.get(colors[1][:3], 0))
                bins[pair['kind']].append(step)
                if step > 1:
                    reviews.append({'kind': pair['kind'], 'islands': [a['island'], b['island']], 't': t, 'rgba': colors})
        stats = {kind: {'samples': len(v), 'different': sum(x != 0 for x in v), 'maxPaletteStepDelta': max(v), 'overOneStep': sum(x > 1 for x in v)} for kind, v in bins.items()}
        seams.append({'name': part['name'], 'statistics': stats, 'offPalette': unknown, 'transparent': transparent, 'overOneStepReview': reviews[:40]})
        if part['unmatchedEdges'] or part['maxPositionError'] > .00001 or part['sharedPaths']:
            failures.append(part['name'] + ': 3D boundary correspondence')
        if reviews or unknown or transparent:
            failures.append(part['name'] + ': seam review required')
    padding = padding_coverage(manifest, root / 'Layers')
    failures += [row['name'] + ': reference mismatch' for row in reference if row['referenceMismatchCount']]
    failures += [row['atlas'] + ': padding' for row in padding if row.get('missingOpaqueBasePixels') or row.get('status') == 'base_layer_missing']
    palette_rgb = {tuple(int(c[j:j+2], 16) for j in (1, 3, 5)) for ramp in palette['ramps'].values() for c in ramp}
    atlas_palette = []
    # 미사용 영역 배경색을 제외하고 실제 표면과 여백의 팔레트를 검사한다.
    for ai, image in enumerate(images):
        name = manifest['atlases'][ai]['name']
        final = Image.open(root / 'Textures' / (name + '_PIXEL.png')).convert('RGBA')
        owner = np.asarray(Image.open(root / 'Source/Ownership' / (name + '_OWNERSHIP.png'))) > 0
        pixels_rgba = np.asarray(final)
        used = {tuple(v) for v in np.unique(pixels_rgba[owner,:3], axis=0)}
        off_palette = len(used - palette_rgb)
        merged = Image.new('RGBA', (512,512), (20,25,23,255))
        for layer in ['01_Base','02_Shading','03_Wear']:
            merged.alpha_composite(Image.open(root / 'Layers' / (name + '_' + layer + '.png')).convert('RGBA'))
        layer_mismatch = int(np.any(np.asarray(merged) != pixels_rgba, axis=2).sum())
        with zipfile.ZipFile(root / 'Layers' / (name + '.ora')) as archive:
            ora = Image.open(BytesIO(archive.read('mergedimage.png'))).convert('RGBA')
            ora_mismatch = int(np.any(np.asarray(ora) != pixels_rgba, axis=2).sum())
        atlas_palette.append({'atlas': name, 'sha256': image['sha256'], 'width': image['width'], 'height': image['height'],
                              'usedPaletteColors': len(used), 'offPaletteColors': off_palette, 'layerCompositeMismatchPixels': layer_mismatch, 'oraMergedMismatchPixels': ora_mismatch})
        if off_palette or layer_mismatch or ora_mismatch:
            failures.append(name + ': palette or editable source consistency')
    result = {'coordinateSpace': 'mesh-local / atlas bottom-left texels',
              'referenceStyle': 'subtle molded rubber pigment bands in mesh-local coordinates, main thresholds -0.65/+0.78, auxiliary/external -0.65; real lighting is NOT baked',
              'manifestSha256': digest(manifest_path), 'atlases': atlas_palette,
              'boundaryCorrespondence': [{'name': p['name'], 'islands': p['islands'], 'pairs': len(p['pairs']), 'unmatched': len(p['unmatchedEdges']), 'maxPositionError': p['maxPositionError']} for p in parts],
              'surfaceReference': reference, 'pngSeams': seams, 'paddingCoverage': padding,
              'interpretation': 'one-step differences are expected nearest-texel threshold quantization; every surface texel is independently checked; over-one-step differences require review, not automatic acceptance',
              'hardFailures': failures}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(result, ensure_ascii=False))
    return 1 if failures else 0


if __name__ == '__main__':
    raise SystemExit(main())
