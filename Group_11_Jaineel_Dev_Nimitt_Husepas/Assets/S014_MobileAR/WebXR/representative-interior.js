import * as THREE from "three";

function addBox(group, collisions, name, center, size, material, collidable = true) {
  const mesh = new THREE.Mesh(new THREE.BoxGeometry(size[0], size[1], size[2]), material);
  mesh.name = name;
  mesh.position.set(center[0], center[1], center[2]);
  mesh.castShadow = true;
  mesh.receiveShadow = true;
  group.add(mesh);
  if (collidable) collisions.push(mesh);
  return mesh;
}

function addWall(group, collisions, name, center, size, material) {
  return addBox(group, collisions, name, center, size, material);
}

function addRail(group, name, start, end, radius, material) {
  const from = new THREE.Vector3(...start);
  const to = new THREE.Vector3(...end);
  const difference = to.clone().sub(from);
  const rail = new THREE.Mesh(
    new THREE.CylinderGeometry(radius, radius, difference.length(), 8),
    material
  );
  rail.name = name;
  rail.position.copy(from).add(to).multiplyScalar(0.5);
  rail.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), difference.normalize());
  group.add(rail);
}

export function createRepresentativeInterior(THREE, layout) {
  const group = new THREE.Group();
  group.name = "RepresentativeInterior_BrowserOnly";
  const collisions = [];
  const { origin, width, depth, wallHeight, floorRise, entrance } = layout;
  const halfWidth = width / 2;
  const halfDepth = depth / 2;
  const front = origin[2] - halfDepth;
  const back = origin[2] + halfDepth;
  const left = origin[0] - halfWidth;
  const right = origin[0] + halfWidth;
  const ground = origin[1];
  const upper = ground + floorRise;
  const roof = upper + wallHeight;

  const floorMaterial = new THREE.MeshStandardMaterial({ color: 0xb6c4c6, roughness: 0.92 });
  const wallMaterial = new THREE.MeshStandardMaterial({ color: 0xc6d2d1, roughness: 0.9 });
  const roofMaterial = new THREE.MeshStandardMaterial({
    color: 0x72848a,
    roughness: 0.95,
    side: THREE.DoubleSide
  });
  const trimMaterial = new THREE.MeshStandardMaterial({ color: 0x354a53, roughness: 0.8 });
  const stairMaterial = new THREE.MeshStandardMaterial({ color: 0x8b9698, roughness: 0.85 });
  const railMaterial = new THREE.MeshStandardMaterial({ color: 0x435b62, metalness: 0.35, roughness: 0.6 });
  const slabThickness = 0.12;
  const floorLevel = (height) => height - slabThickness / 2;

  addBox(
    group,
    collisions,
    "GroundFloor_Representative",
    [origin[0], floorLevel(ground), origin[2]],
    [width, slabThickness, depth],
    floorMaterial
  );

  const opening = layout.stairwell;
  const openingLeft = origin[0] + opening.minX;
  const openingRight = origin[0] + opening.maxX;
  const openingFront = origin[2] + opening.minZ;
  const openingBack = origin[2] + opening.maxZ;
  const floorSections = [
    ["UpperFloor_Front", origin[2] - halfDepth / 2, depth / 2],
    ["UpperFloor_Back", openingBack + (back - openingBack) / 2, back - openingBack],
    ["UpperFloor_Left", left + (openingLeft - left) / 2, openingLeft - left, openingFront, openingBack - openingFront],
    ["UpperFloor_Right", openingRight + (right - openingRight) / 2, right - openingRight, openingFront, openingBack - openingFront]
  ];
  addBox(
    group,
    collisions,
    floorSections[0][0],
    [origin[0], floorLevel(upper), floorSections[0][1]],
    [width, slabThickness, floorSections[0][2]],
    floorMaterial
  );
  addBox(
    group,
    collisions,
    floorSections[1][0],
    [origin[0], floorLevel(upper), floorSections[1][1]],
    [width, slabThickness, floorSections[1][2]],
    floorMaterial
  );
  addBox(
    group,
    collisions,
    floorSections[2][0],
    [floorSections[2][1], floorLevel(upper), (openingFront + openingBack) / 2],
    [floorSections[2][2], slabThickness, floorSections[2][4]],
    floorMaterial
  );
  addBox(
    group,
    collisions,
    floorSections[3][0],
    [floorSections[3][1], floorLevel(upper), (openingFront + openingBack) / 2],
    [floorSections[3][2], slabThickness, floorSections[3][4]],
    floorMaterial
  );

  const entranceHalf = entrance.width / 2;
  const entranceLeft = origin[0] + entrance.centerX - entranceHalf;
  const entranceRight = origin[0] + entrance.centerX + entranceHalf;
  const wallThickness = 0.2;
  const levelHeight = wallHeight;
  const lowerWallCenter = ground + levelHeight / 2;
  const upperWallCenter = upper + levelHeight / 2;
  addWall(group, collisions, "GroundWall_Left", [left, lowerWallCenter, origin[2]], [wallThickness, levelHeight, depth], wallMaterial);
  addWall(group, collisions, "GroundWall_Right", [right, lowerWallCenter, origin[2]], [wallThickness, levelHeight, depth], wallMaterial);
  addWall(group, collisions, "GroundWall_Back", [origin[0], lowerWallCenter, back], [width, levelHeight, wallThickness], wallMaterial);
  addWall(group, collisions, "GroundWall_FrontLeft", [(left + entranceLeft) / 2, lowerWallCenter, front], [entranceLeft - left, levelHeight, wallThickness], wallMaterial);
  addWall(group, collisions, "GroundWall_FrontRight", [(entranceRight + right) / 2, lowerWallCenter, front], [right - entranceRight, levelHeight, wallThickness], wallMaterial);
  addWall(group, collisions, "GroundWall_EntranceLintel", [origin[0] + entrance.centerX, ground + levelHeight - 0.28, front], [entrance.width, 0.56, wallThickness], wallMaterial);
  addWall(group, collisions, "UpperWall_Left", [left, upperWallCenter, origin[2]], [wallThickness, levelHeight, depth], wallMaterial);
  addWall(group, collisions, "UpperWall_Right", [right, upperWallCenter, origin[2]], [wallThickness, levelHeight, depth], wallMaterial);
  addWall(group, collisions, "UpperWall_Back", [origin[0], upperWallCenter, back], [width, levelHeight, wallThickness], wallMaterial);
  addWall(group, collisions, "UpperWall_Front", [origin[0], upperWallCenter, front], [width, levelHeight, wallThickness], wallMaterial);

  addBox(group, collisions, "Roof_Representative", [origin[0], roof, origin[2]], [width, 0.14, depth], roofMaterial, false);
  addBox(group, [], "EntranceFrame_Left", [entranceLeft, ground + 1.1, front], [0.12, 2.2, 0.18], trimMaterial, false);
  addBox(group, [], "EntranceFrame_Right", [entranceRight, ground + 1.1, front], [0.12, 2.2, 0.18], trimMaterial, false);

  function addFlight(start, end, count, name) {
    const first = new THREE.Vector3(...start);
    const last = new THREE.Vector3(...end);
    const direction = last.clone().sub(first);
    const horizontal = new THREE.Vector3(direction.x, 0, direction.z);
    const depthPerStep = horizontal.length() / count;
    for (let index = 0; index < count; index++) {
      const progress = (index + 0.5) / count;
      const center = first.clone().addScaledVector(direction, progress);
      center.y = first.y + direction.y * (index + 1) / count - 0.09;
      const tread = new THREE.Mesh(
        new THREE.BoxGeometry(1.2, 0.18, depthPerStep + 0.04),
        stairMaterial
      );
      tread.name = name + "_Tread_" + (index + 1);
      tread.position.copy(center);
      tread.rotation.y = Math.atan2(horizontal.x, horizontal.z);
      tread.castShadow = true;
      tread.receiveShadow = true;
      group.add(tread);
    }
    return { first, last };
  }

  const stairBottom = [origin[0] - 2, ground, origin[2] + 4];
  const stairMidpoint = [origin[0] - 2, ground + 1, origin[2] + 1.5];
  const stairTop = [origin[0] + 1.5, upper, origin[2] + 1.5];
  addFlight(stairBottom, stairMidpoint, 8, "StairFlight_Ground");
  addFlight(stairMidpoint, stairTop, 8, "StairFlight_Upper");
  addRail(group, "StairRail_Ground_Left", [stairBottom[0] - 0.62, ground + 0.92, stairBottom[2]], [stairMidpoint[0] - 0.62, ground + 1.92, stairMidpoint[2]], 0.045, railMaterial);
  addRail(group, "StairRail_Ground_Right", [stairBottom[0] + 0.62, ground + 0.92, stairBottom[2]], [stairMidpoint[0] + 0.62, ground + 1.92, stairMidpoint[2]], 0.045, railMaterial);
  addRail(group, "StairRail_Upper_Left", [stairMidpoint[0], ground + 1.92, stairMidpoint[2] - 0.62], [stairTop[0], upper + 0.92, stairTop[2] - 0.62], 0.045, railMaterial);
  addRail(group, "StairRail_Upper_Right", [stairMidpoint[0], ground + 1.92, stairMidpoint[2] + 0.62], [stairTop[0], upper + 0.92, stairTop[2] + 0.62], 0.045, railMaterial);

  return {
    group,
    collisionObjects: collisions,
    bounds: {
      minX: left,
      maxX: right,
      minY: ground,
      maxY: roof + 0.14,
      minZ: front,
      maxZ: back
    }
  };
}
