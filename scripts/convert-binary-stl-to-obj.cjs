const fs = require('fs');
const path = require('path');

const [, , inputPath, outputPath, scaleText = '1'] = process.argv;
if (!inputPath || !outputPath) throw new Error('Usage: node convert-binary-stl-to-obj.cjs <input.stl> <output.obj> [scale]');

const source = fs.readFileSync(inputPath);
if (source.length < 84) throw new Error('STL is too short.');
const triangleCount = source.readUInt32LE(80);
if (84 + triangleCount * 50 !== source.length) throw new Error('Only binary STL files are supported.');
const scale = Number(scaleText);
if (!Number.isFinite(scale) || scale <= 0) throw new Error('Scale must be a positive number.');

const triangles = [];
let minimum = [Infinity, Infinity, Infinity];
let maximum = [-Infinity, -Infinity, -Infinity];
for (let triangle = 0, offset = 84; triangle < triangleCount; triangle++, offset += 50) {
  const vertices = [];
  for (let vertex = 0; vertex < 3; vertex++) {
    const sourceOffset = offset + 12 + vertex * 12;
    const converted = [
      source.readFloatLE(sourceOffset),
      source.readFloatLE(sourceOffset + 8),
      -source.readFloatLE(sourceOffset + 4)
    ];
    for (let axis = 0; axis < 3; axis++) {
      minimum[axis] = Math.min(minimum[axis], converted[axis]);
      maximum[axis] = Math.max(maximum[axis], converted[axis]);
    }
    vertices.push(converted);
  }
  triangles.push(vertices);
}

const centerX = (minimum[0] + maximum[0]) / 2;
const centerZ = (minimum[2] + maximum[2]) / 2;
const vertexIds = new Map();
const vertices = [];
const faces = [];
for (const triangle of triangles) {
  const face = [];
  for (const vertex of triangle) {
    const normalized = [(vertex[0] - centerX) * scale, (vertex[1] - minimum[1]) * scale, (vertex[2] - centerZ) * scale];
    const key = normalized.map(value => value.toFixed(6)).join(',');
    let id = vertexIds.get(key);
    if (!id) {
      id = vertices.length + 1;
      vertexIds.set(key, id);
      vertices.push(normalized);
    }
    face.push(id);
  }
  faces.push(face);
}

const lines = [
  '# Converted from a binary STL for the private Down Range Unity tactical resolver.',
  `# Source: ${path.basename(inputPath)}`,
  `o ${path.basename(outputPath, path.extname(outputPath))}`,
  ...vertices.map(vertex => `v ${vertex[0].toFixed(6)} ${vertex[1].toFixed(6)} ${vertex[2].toFixed(6)}`),
  's 1',
  ...faces.map(face => `f ${face.join(' ')}`),
  ''
];
fs.mkdirSync(path.dirname(outputPath), { recursive: true });
fs.writeFileSync(outputPath, lines.join('\n'), 'utf8');
console.log(`${path.basename(inputPath)} -> ${path.basename(outputPath)} (${triangleCount} triangles, ${vertices.length} unique vertices)`);
