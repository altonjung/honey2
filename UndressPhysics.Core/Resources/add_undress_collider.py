import zipfile
import os
import shutil
import argparse
import xml.etree.ElementTree as ET
import copy

# -----------------------------
# Template XML string
# -----------------------------
TEMPLATE_XML_TOP = """
<cloth>
      <CapsuleCollider boneName="cf_J_Shoulder_L" radius="0.52" center="1.00, -0.01, 0.0" height="2.0" direction="0" />    
      <CapsuleCollider boneName="cf_J_Shoulder_R" radius="0.52" center="-1.00, -0.01, 0.0" height="2.0" direction="0" />
      <CapsuleCollider boneName="cf_J_Spine02_s" radius="0.91" center="0.00, -0.10, 0.00" height="3.90" direction="1" />
      <CapsuleCollider boneName="cf_J_Kosi01_s" radius="1.05" center="0.00, -0.15, -0.10" height="3.00" direction="1" />
      <CapsuleCollider boneName="cf_J_Kosi02_s" radius="1.15" center="0.00, 0.00, -0.13" height="3.00" direction="1" />
</cloth>
"""
TEMPLATE_XML_BOTTOM = """
<cloth>
      <SphereColliderPair>
        <first boneName="cf_J_LegUp01_s_L" radius="0.88" center="0.09, -0.04, 0.08" />
        <second boneName="cf_J_LegKnee_low_s_L" radius="0.8" center="0.06, -0.30, -0.35" />
      </SphereColliderPair>
      <SphereColliderPair>
        <first boneName="cf_J_LegUp01_s_L" radius="0.88" center="0.09, -0.04, 0.08" />
        <second boneName="cf_J_LegUp02_s_L" radius="0.85" center="-0.05, 0.76, 0.1" />
      </SphereColliderPair>
      <SphereColliderPair>
        <first boneName="cf_J_LegUp01_s_L" radius="0.88" center="0.09, -0.04, 0.08" />
        <second boneName="cf_J_LegUp03_s_L" radius="0.60" center="-0.24, 0.00, 0.13" />
      </SphereColliderPair>
      <SphereColliderPair>
        <first boneName="cf_J_LegUp03_s_L" radius="0.60" center="-0.24, 0.00, 0.13" />
        <second boneName="cf_J_LegKnee_low_s_L" radius="0.8" center="0.06, -0.30, -0.35" />
      </SphereColliderPair>
      <SphereColliderPair>
        <first boneName="cf_J_LegUp02_s_L" radius="0.85" center="-0.05, 0.76, 0.1" />
        <second boneName="cf_J_LegUp03_s_L" radius="0.60" center="-0.24, 0.00, 0.13" />
      </SphereColliderPair>
      <SphereColliderPair>
        <first boneName="cf_J_LegUp01_s_R" radius="0.88" center="-0.09, -0.04, 0.08" />
        <second boneName="cf_J_LegKnee_low_s_R" radius="0.8" center="-0.06, -0.30, -0.35" />
      </SphereColliderPair>
      <SphereColliderPair>
        <first boneName="cf_J_LegUp01_s_R" radius="0.88" center="-0.09, -0.04, 0.08" />
        <second boneName="cf_J_LegUp02_s_R" radius="0.85" center="0.05, 0.76, 0.1" />
      </SphereColliderPair>
      <SphereColliderPair>
        <first boneName="cf_J_LegUp01_s_R" radius="0.88" center="-0.09, -0.04, 0.08" />
        <second boneName="cf_J_LegUp03_s_R" radius="0.60" center="0.16, 0.00, 0.13" />
      </SphereColliderPair>
      <SphereColliderPair>
        <first boneName="cf_J_LegUp03_s_R" radius="0.60" center="0.16, 0.00, 0.13" />
        <second boneName="cf_J_LegKnee_low_s_R" radius="0.8" center="-0.06, -0.30, -0.35" />
      </SphereColliderPair>
      <SphereColliderPair>
        <first boneName="cf_J_LegUp02_s_R" radius="0.85" center="0.05, 0.76, 0.1" />
        <second boneName="cf_J_LegUp03_s_R" radius="0.60" center="0.16, 0.00, 0.13" />
      </SphereColliderPair>

      <SphereColliderPair>
        <first boneName="cf_J_LegKnee_low_s_L" radius="0.8" center="0.06, -0.30, -0.35" />
        <second boneName="cf_J_LegLow01_s_L" radius="0.65" center="-0.07, -1.41, -0.25" />
      </SphereColliderPair>
      <SphereColliderPair>
        <first boneName="cf_J_LegLow01_s_L" radius="0.65" center="-0.07, -1.41, -0.25" />
        <second boneName="cf_J_LegLow02_s_L" radius="0.50" center="-0.06, 0.00, -0.20" />
      </SphereColliderPair>
      <SphereColliderPair>
        <first boneName="cf_J_LegLow02_s_L" radius="0.50" center="-0.06, 0.00, -0.20" />
        <second boneName="cf_J_LegLow03_s_L" radius="0.38" center="0.07, -1.07, -0.10" />
      </SphereColliderPair>
      <SphereColliderPair>
        <first boneName="cf_J_LegKnee_low_s_L" radius="0.8" center="0.06, -0.30, -0.31" />
        <second boneName="cf_J_LegLow02_s_L" radius="0.50" center="-0.06, 0.00, -0.20" />
      </SphereColliderPair>
      <SphereColliderPair>
        <first boneName="cf_J_LegLow03_s_L" radius="0.38" center="0.07, -1.07, -0.10" />
        <second boneName="cf_J_Foot02_L" radius="0.38" center="0.00, -0.32, 1.30" />
      </SphereColliderPair>
      <SphereColliderPair>
        <first boneName="cf_J_LegKnee_low_s_R" radius="0.8" center="-0.06, -0.30, -0.35" />
        <second boneName="cf_J_LegLow01_s_R" radius="0.65" center="0.07, -1.41, -0.25" />
      </SphereColliderPair>
      <SphereColliderPair>
        <first boneName="cf_J_LegLow01_s_R" radius="0.65" center="0.07, -1.41, -0.25" />
        <second boneName="cf_J_LegLow02_s_R" radius="0.50" center="0.06, 0.00, -0.20" />
      </SphereColliderPair>
      <SphereColliderPair>
        <first boneName="cf_J_LegLow02_s_R" radius="0.50" center="0.06, 0.00, -0.20" />
        <second boneName="cf_J_LegLow03_s_R" radius="0.38" center="-0.07, -1.07, -0.10" />
      </SphereColliderPair>
      <SphereColliderPair>
        <first boneName="cf_J_LegKnee_low_s_R" radius="0.8" center="-0.06, -0.30, -0.35" />
        <second boneName="cf_J_LegLow02_s_R" radius="0.50" center="0.06, 0.00, -0.20" />
      </SphereColliderPair>
      <SphereColliderPair>
        <first boneName="cf_J_LegLow03_s_R" radius="0.38" center="-0.07, -1.07, -0.10" />
        <second boneName="cf_J_Foot02_R" radius="0.38" center="0.00, -0.32, 1.30" />
      </SphereColliderPair>

      <CapsuleCollider boneName="cf_N_height" radius="60.00" center="0.00, -60.00, 0.00" height="1.00" direction="1" />

</cloth>
"""

# -----------------------------
# Extract zip
# -----------------------------
def extract_zip(zip_path, extract_dir):
    with zipfile.ZipFile(zip_path, 'r') as z:
        z.extractall(extract_dir)


# -----------------------------
# Repack zip
# -----------------------------
def repack_zip(src_dir, output_zip):
    with zipfile.ZipFile(output_zip, 'w', zipfile.ZIP_DEFLATED) as z:
        for root, dirs, files in os.walk(src_dir):
            for f in files:
                full = os.path.join(root, f)
                rel = os.path.relpath(full, src_dir)
                z.write(full, rel)


# -----------------------------
# Replace collider inside cloth
# -----------------------------
def patch_single_cloth(manifest_cloth):

    category = (manifest_cloth.attrib.get("category") or "").lower()

    template_roots = []

    if "_top" in category:
        print(f"[MERGE-TOP+BOTTOM] cloth category='{category}'")
        template_roots.append(ET.fromstring(TEMPLATE_XML_TOP))
        template_roots.append(ET.fromstring(TEMPLATE_XML_BOTTOM))

    elif "_bot" in category:
        print(f"[MERGE-BOTTOM] cloth category='{category}'")
        template_roots.append(ET.fromstring(TEMPLATE_XML_BOTTOM))

    else:
        print(f"[SKIP] cloth category='{category}'")
        return False

    # -------------------------------------------------
    # 1. 기존 collider 정보 수집
    # -------------------------------------------------
    existing_capsule = set()
    existing_pair_first = set()

    for node in manifest_cloth:

        # CapsuleCollider
        if node.tag == "CapsuleCollider":
            bone = node.attrib.get("boneName")
            if bone:
                existing_capsule.add(bone)

        # SphereColliderPair
        elif node.tag == "SphereColliderPair":
            first = node.find("first")
            if first is not None:
                bone = first.attrib.get("boneName")
                if bone:
                    existing_pair_first.add(bone)

    # -------------------------------------------------
    # 2. Template 병합
    # -------------------------------------------------
    added_any = False

    for template_root in template_roots:

        for child in template_root:

            # ---------- Capsule ----------
            if child.tag == "CapsuleCollider":

                bone = child.attrib.get("boneName")

                if bone in existing_capsule:
                    continue

                manifest_cloth.append(copy.deepcopy(child))
                existing_capsule.add(bone)
                added_any = True

            # ---------- SphereColliderPair ----------
            elif child.tag == "SphereColliderPair":

                first = child.find("first")
                if first is None:
                    continue

                bone = first.attrib.get("boneName")

                # ⭐ first boneName 기준 skip
                if bone in existing_pair_first:
                    continue

                manifest_cloth.append(copy.deepcopy(child))
                existing_pair_first.add(bone)
                added_any = True

    return added_any


# -----------------------------
# Patch manifest.xml
# -----------------------------
def patch_manifest(manifest_path):

    tree = ET.parse(manifest_path)
    root = tree.getroot()

    cloth_nodes = root.findall(".//cloth")

    if not cloth_nodes:
        print("[WARNING] No <cloth> tags found")
        return False

    modified_any = False

    for cloth in cloth_nodes:
        if patch_single_cloth(cloth):
            modified_any = True

    if modified_any:
        tree.write(manifest_path, encoding="utf-8", xml_declaration=True)

    return modified_any


# -----------------------------
# Process single zipmod
# -----------------------------
def process(zipmod_path, output_dir=None):

    base_dir = os.path.dirname(zipmod_path)
    filename = os.path.basename(zipmod_path)
    name = os.path.splitext(filename)[0]

    temp_dir = os.path.join(base_dir, "_temp_extract")

    if os.path.exists(temp_dir):
        shutil.rmtree(temp_dir)

    os.makedirs(temp_dir)

    print(f"\nProcessing: {filename}")

    # Extract
    extract_zip(zipmod_path, temp_dir)

    manifest = os.path.join(temp_dir, "manifest.xml")

    if not os.path.exists(manifest):
        print("[WARNING] manifest.xml not found")
        shutil.rmtree(temp_dir)
        return

    # Patch
    patched = patch_manifest(manifest)

    if not patched:
        print("[INFO] No modification needed")
        shutil.rmtree(temp_dir)
        return

    # output 위치 결정
    if output_dir:
        output_zip = os.path.join(output_dir, f"{name}_patched.zipmod")
    else:
        output_zip = os.path.join(base_dir, f"{name}_patched.zipmod")

    # Repack
    repack_zip(temp_dir, output_zip)

    print(f"[DONE] {output_zip}")

    shutil.rmtree(temp_dir)


# -----------------------------
# Main
# -----------------------------
if __name__ == "__main__":
    parser = argparse.ArgumentParser(
        description="Replace cloth colliders with template"
    )

    group = parser.add_mutually_exclusive_group(required=True)

    group.add_argument("--zipmod", help="Input single zip or zipmod file")
    group.add_argument("--folder", help="Process all zip / zipmod files inside folder")

    args = parser.parse_args()

    # -----------------------------
    # Single file mode
    # -----------------------------
    if args.zipmod:
        if not os.path.exists(args.zipmod):
            print("File not found:", args.zipmod)
        else:
            process(args.zipmod)

    # -----------------------------
    # Folder mode
    # -----------------------------
    if args.folder:

        if not os.path.isdir(args.folder):
            print("Folder not found:", args.folder)
            exit()

        # ⭐ output 폴더 생성
        output_dir = os.path.join(args.folder, "output")
        os.makedirs(output_dir, exist_ok=True)

        targets = []

        for f in os.listdir(args.folder):
            if f.lower().endswith((".zip", ".zipmod")):
                targets.append(os.path.join(args.folder, f))

        if not targets:
            print("[INFO] No zip or zipmod files found")
            exit()

        print(f"[INFO] Found {len(targets)} files")

        for path in targets:
            try:
                process(path, output_dir)
            except Exception as e:
                print(f"[ERROR] Failed: {path}")
                print(e)
