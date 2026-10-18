import { readFile, writeFile } from "node:fs/promises";

const routePath = new URL("../WebXR/route-data.json", import.meta.url);
const outputPath = new URL("./S014_Campus_Floor_Plan.svg", import.meta.url);
const data = JSON.parse(await readFile(routePath, "utf8"));
const graph = data.campusPrototypeGraph;
const nodeById = new Map(graph.nodes.map(node => [node.id, node]));
const project = ([x, , z]) => [430 + x * 3, 340 - z * 3];
const xml = value => String(value).replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;");
const edgePath = graph.edges.map(edge => {
  const a = project(nodeById.get(edge.from).position);
  const b = project(nodeById.get(edge.to).position);
  return `M${a[0].toFixed(1)},${a[1].toFixed(1)}L${b[0].toFixed(1)},${b[1].toFixed(1)}`;
}).join("");
const obstacles = graph.obstacleBounds.map(obstacle => {
  if (obstacle.shape === "circle") {
    const [cx, cy] = project([obstacle.centerX, 0, obstacle.centerZ]);
    return `<circle cx="${cx}" cy="${cy}" r="${(obstacle.radius + obstacle.clearance) * 3}"/>`;
  }
  const [x, y] = project([obstacle.minX - obstacle.clearance, 0, obstacle.maxZ + obstacle.clearance]);
  const width = (obstacle.maxX - obstacle.minX + 2 * obstacle.clearance) * 3;
  const height = (obstacle.maxZ - obstacle.minZ + 2 * obstacle.clearance) * 3;
  return `<rect x="${x}" y="${y}" width="${width}" height="${height}"/>`;
}).join("\n");
const destinations = data.destinations.map((destination, index) => {
  const node = nodeById.get(destination.campusNodeId);
  const [x, y] = project(node.position);
  const color = destination.campusFloor === "First" ? "#9b5de5" : "#d9485f";
  return `<g><circle cx="${x}" cy="${y}" r="3.4" fill="${color}" stroke="white" stroke-width="1"/><text x="${x + 4}" y="${y - 3}" class="number">${index + 1}</text></g>`;
}).join("\n");
const legend = data.destinations.map((destination, index) => {
  const floor = destination.campusFloor === "First" ? " · 1st floor" : "";
  const inferred = destination.coverage === "inferred-campus-approach" ? " · inferred" : "";
  return `<text x="780" y="${154 + index * 25}" class="legend"><tspan class="number">${index + 1}.</tspan> ${xml(destination.name)}${floor}${inferred}</text>`;
}).join("\n");
const prototype = data.representativeInterior;
const [px, py] = project([prototype.origin[0], 0, prototype.origin[2]]);
const svg = `<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="900" viewBox="0 0 1200 900" role="img" aria-labelledby="title desc">
<title id="title">Imported campus walk-surface graph and destination approaches</title>
<desc id="desc">Unity scene-coordinate plan generated from the imported campus path mesh. Building footprints are obstacles; purple markers are first-floor prototype labs and red markers are inferred ground-floor approaches.</desc>
<style>text{font-family:Arial,sans-serif;fill:#24374b}.title{font-size:23px;font-weight:bold}.sub{font-size:12px;fill:#526477}.legend{font-size:12px}.number{font-size:10px;font-weight:bold;fill:#233}.building{fill:#b88f80;fill-opacity:.72;stroke:#704f46;stroke-width:1}.path{fill:none;stroke:#14848b;stroke-width:.8;stroke-opacity:.48}.connector{fill:none;stroke:#d68b24;stroke-width:1.7;stroke-dasharray:3 2}.obstacle{fill:#8b665d;fill-opacity:.82;stroke:#593d37;stroke-width:1.2}</style>
<rect width="100%" height="100%" fill="#f7fafc"/><text x="28" y="34" class="title">Campus route graph from imported Unity geometry</text>
<text x="30" y="56" class="sub">1,576 nodes · 4,748 walkable edges · weighted Dijkstra · building clearance applied; inferred links and facility assignments require field verification</text>
<rect x="95" y="145" width="610" height="585" rx="5" fill="#eaf2e8" stroke="#839981"/>
<g class="path"><path d="${edgePath}"/></g>
<g class="obstacle">${obstacles}</g>
<rect x="${(px - 6.1 * 3).toFixed(1)}" y="${(py - 6.24 * 3).toFixed(1)}" width="36.6" height="37.4" fill="#4386a0" fill-opacity=".8" stroke="#16516a" stroke-width="1.4"/>
<text x="${(px - 20).toFixed(1)}" y="${(py + 56).toFixed(1)}" class="number">Prototype labs · inferred front door</text>
<g>${destinations}</g>
<circle cx="${project(nodeById.get(graph.startNodeId).position)[0]}" cy="${project(nodeById.get(graph.startNodeId).position)[1]}" r="5" fill="#32a852" stroke="white" stroke-width="1.5"/>
<text x="780" y="130" class="title" style="font-size:16px">22 distinct destination terminals</text>${legend}
<line x1="780" y1="728" x2="814" y2="728" class="path"/><text x="824" y="732" class="sub">Imported paved surface graph</text>
<line x1="780" y1="750" x2="814" y2="750" class="connector"/><text x="824" y="754" class="sub">Inferred connector</text>
<rect x="780" y="766" width="24" height="14" class="obstacle"/><text x="824" y="778" class="sub">Building footprint / clearance obstacle</text>
<circle cx="792" cy="800" r="5" fill="#32a852"/><text x="824" y="804" class="sub">Campus entrance route start</text>
<text x="28" y="856" class="sub">Scene coordinates; route distances are shortest within this graph. Generic building names, front gates, and unlabelled destinations are inferred from geometry.</text>
</svg>`;
await writeFile(outputPath, svg, "utf8");
console.log(`Updated ${outputPath.pathname} from ${graph.nodes.length} nodes and ${graph.edges.length} edges.`);
