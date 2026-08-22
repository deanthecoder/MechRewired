#!/usr/bin/env python3
"""Build Quest-scale rock meshes from the downloaded high-poly source pack.

The source archive remains outside the repository. This tool extracts it temporarily, uses
Assimp to convert the FBX to OBJ, then applies deterministic vertex clustering to selected
rocks. The output is six one-metre-at-the-widest low-poly OBJ meshes and 1K shared textures
that Godot imports directly.
"""

from __future__ import annotations

import argparse
import subprocess
import tempfile
import zipfile
from dataclasses import dataclass
from pathlib import Path

from PIL import Image


SELECTED_ROCKS = (0, 3, 7, 8, 12, 16)
TARGET_TRIANGLES = (72, 96, 128, 80, 112, 144)
TEXTURE_NAMES = {
    "Rock20_col.jpg": "rock_color.jpg",
    "Rock20_nrm.jpg": "rock_normal.jpg",
    "Rock20_rgh.jpg": "rock_roughness.jpg",
}


@dataclass(frozen=True)
class FaceVertex:
    position: int
    texcoord: int | None


@dataclass
class ObjMesh:
    name: str
    faces: list[tuple[FaceVertex, FaceVertex, FaceVertex]]


def parse_face_vertex(value: str) -> FaceVertex:
    parts = value.split("/")
    return FaceVertex(int(parts[0]) - 1, int(parts[1]) - 1 if len(parts) > 1 and parts[1] else None)


def parse_obj(path: Path) -> tuple[list[tuple[float, float, float]], list[tuple[float, float]], list[ObjMesh]]:
    positions: list[tuple[float, float, float]] = []
    texcoords: list[tuple[float, float]] = []
    meshes: list[ObjMesh] = []
    current: ObjMesh | None = None
    for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
        values = line.split()
        if not values:
            continue
        if values[0] in ("o", "g"):
            current = ObjMesh("_".join(values[1:]) or f"rock_{len(meshes):02d}", [])
            meshes.append(current)
        elif values[0] == "v":
            positions.append(tuple(map(float, values[1:4])))
        elif values[0] == "vt":
            texcoords.append((float(values[1]), float(values[2])))
        elif values[0] == "f":
            if current is None:
                current = ObjMesh(f"rock_{len(meshes):02d}", [])
                meshes.append(current)
            vertices = [parse_face_vertex(value) for value in values[1:]]
            for index in range(1, len(vertices) - 1):
                current.faces.append((vertices[0], vertices[index], vertices[index + 1]))
    return positions, texcoords, [mesh for mesh in meshes if mesh.faces]


def simplify_mesh(
    mesh: ObjMesh,
    positions: list[tuple[float, float, float]],
    texcoords: list[tuple[float, float]],
    target_triangles: int,
) -> tuple[list[tuple[float, float, float]], list[tuple[float, float]], list[tuple[int, int, int]]]:
    used = sorted({vertex.position for face in mesh.faces for vertex in face})
    source_positions = [positions[index] for index in used]
    minimum = tuple(min(value[axis] for value in source_positions) for axis in range(3))
    maximum = tuple(max(value[axis] for value in source_positions) for axis in range(3))
    extent = tuple(maximum[axis] - minimum[axis] for axis in range(3))
    best: tuple[int, list[tuple[float, float, float]], list[tuple[float, float]], list[tuple[int, int, int]]] | None = None

    for resolution in range(2, 49):
        clusters: dict[tuple[int, int, int], list[FaceVertex]] = {}
        remapped: dict[FaceVertex, tuple[int, int, int]] = {}
        for face in mesh.faces:
            for vertex in face:
                position = positions[vertex.position]
                key = tuple(
                    min(
                        resolution - 1,
                        int((position[axis] - minimum[axis]) / max(extent[axis], 0.00001) * resolution),
                    )
                    for axis in range(3)
                )
                clusters.setdefault(key, []).append(vertex)
                remapped[vertex] = key

        keys = sorted(clusters)
        index_by_key = {key: index for index, key in enumerate(keys)}
        output_positions: list[tuple[float, float, float]] = []
        output_texcoords: list[tuple[float, float]] = []
        for key in keys:
            vertices = clusters[key]
            output_positions.append(tuple(
                sum(positions[vertex.position][axis] for vertex in vertices) / len(vertices)
                for axis in range(3)
            ))
            mapped_texcoords = [texcoords[vertex.texcoord] for vertex in vertices if vertex.texcoord is not None]
            output_texcoords.append(
                (
                    sum(value[0] for value in mapped_texcoords) / len(mapped_texcoords),
                    sum(value[1] for value in mapped_texcoords) / len(mapped_texcoords),
                ) if mapped_texcoords else (0.0, 0.0)
            )

        output_faces: list[tuple[int, int, int]] = []
        seen_faces: set[tuple[int, int, int]] = set()
        for face in mesh.faces:
            output_face = tuple(index_by_key[remapped[vertex]] for vertex in face)
            if len(set(output_face)) < 3:
                continue
            canonical = tuple(sorted(output_face))
            if canonical not in seen_faces:
                seen_faces.add(canonical)
                output_faces.append(output_face)

        candidate = (abs(len(output_faces) - target_triangles), output_positions, output_texcoords, output_faces)
        if best is None or candidate[0] < best[0]:
            best = candidate

    assert best is not None
    _, output_positions, output_texcoords, output_faces = best
    min_y = min(position[1] for position in output_positions)
    centre_x = (min(position[0] for position in output_positions) + max(position[0] for position in output_positions)) * 0.5
    centre_z = (min(position[2] for position in output_positions) + max(position[2] for position in output_positions)) * 0.5
    widest = max(
        max(position[0] for position in output_positions) - min(position[0] for position in output_positions),
        max(position[2] for position in output_positions) - min(position[2] for position in output_positions),
        0.00001,
    )
    normalised_positions = [
        ((position[0] - centre_x) / widest, (position[1] - min_y) / widest, (position[2] - centre_z) / widest)
        for position in output_positions
    ]
    return normalised_positions, output_texcoords, output_faces


def write_obj(
    path: Path,
    positions: list[tuple[float, float, float]],
    texcoords: list[tuple[float, float]],
    faces: list[tuple[int, int, int]],
) -> None:
    lines = ["mtllib rock.mtl", "o rock", "usemtl DesertRock", "s off"]
    lines.extend(f"v {x:.6f} {y:.6f} {z:.6f}" for x, y, z in positions)
    lines.extend(f"vt {u:.6f} {v:.6f}" for u, v in texcoords)
    lines.extend(f"f {a + 1}/{a + 1} {b + 1}/{b + 1} {c + 1}/{c + 1}" for a, b, c in faces)
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def copy_texture(source: Path, destination: Path) -> None:
    with Image.open(source) as image:
        image = image.convert("RGB")
        image.thumbnail((1024, 1024), Image.Resampling.LANCZOS)
        image.save(destination, quality=92, optimize=True)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True, help="Path to highpoly-rocks-free-download.zip")
    parser.add_argument("--output", type=Path, required=True, help="Godot asset output directory")
    args = parser.parse_args()
    if not args.source.is_file():
        raise SystemExit(f"Rock source archive does not exist: {args.source}")

    args.output.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="mechrewired-rocks-") as temporary_directory:
        temporary = Path(temporary_directory)
        with zipfile.ZipFile(args.source) as archive:
            archive.extract("source/rocks.fbx", temporary)
            for texture in TEXTURE_NAMES:
                archive.extract(f"textures/{texture}", temporary)
        fbx_path = temporary / "source" / "rocks.fbx"
        obj_path = temporary / "rocks.obj"
        subprocess.run(["assimp", "export", str(fbx_path), str(obj_path), "-f", "obj"], check=True)
        positions, texcoords, meshes = parse_obj(obj_path)
        if len(meshes) < max(SELECTED_ROCKS) + 1:
            raise RuntimeError(f"Expected at least 17 rock meshes, found {len(meshes)}.")
        for output_index, (source_index, target_triangles) in enumerate(zip(SELECTED_ROCKS, TARGET_TRIANGLES, strict=True)):
            output_positions, output_texcoords, output_faces = simplify_mesh(
                meshes[source_index], positions, texcoords, target_triangles)
            output_path = args.output / f"rock_{output_index:02d}.obj"
            write_obj(output_path, output_positions, output_texcoords, output_faces)
            print(f"{output_path.name}: {len(output_faces)} triangles from {len(meshes[source_index].faces)}")
        for source_name, output_name in TEXTURE_NAMES.items():
            copy_texture(temporary / "textures" / source_name, args.output / output_name)

    (args.output / "rock.mtl").write_text(
        "newmtl DesertRock\nmap_Kd rock_color.jpg\nmap_Bump rock_normal.jpg\nmap_Ns rock_roughness.jpg\n",
        encoding="utf-8",
    )
    print(f"Wrote Quest-scale rock assets to {args.output}")


if __name__ == "__main__":
    main()
