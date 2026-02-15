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
      <CapsuleCollider boneName="cf_J_ArmUp02_s_L" radius="0.40" center="0.00, 0.00, 0.00" height="2.00" direction="0" />
      <CapsuleCollider boneName="cf_J_ArmUp02_s_R" radius="0.40" center="0.00, 0.00, 0.00" height="2.00" direction="0" /> 
      <CapsuleCollider boneName="cf_J_ArmUp01_s_L" radius="0.40" center="0.00, 0.00, 0.00" height="2.00" direction="0" />
      <CapsuleCollider boneName="cf_J_ArmUp01_s_R" radius="0.40" center="0.00, 0.00, 0.00" height="2.00" direction="0" />    
      <CapsuleCollider boneName="cf_J_Shoulder_L" radius="0.40" center="0.00, 0.00, 0.00" height="2.00" direction="1" />    
      <CapsuleCollider boneName="cf_J_Shoulder_R" radius="0.40" center="0.00, 0.00, 0.00" height="2.00" direction="1" />    
      <CapsuleCollider boneName="cf_J_Spine01_s" radius="0.65" center="0.00, 0.00, 0.00" height="2.80" direction="0" />
      <CapsuleCollider boneName="cf_J_Spine02_s" radius="0.92" center="0.00, 0.10, 0.20" height="3.00" direction="1" />
      <CapsuleCollider boneName="cf_J_Spine03_s" radius="0.65" center="0.00, 0.20, 0.20" height="2.60" direction="0" />
      <CapsuleCollider boneName="cf_J_Kosi01_s" radius="1.00" center="0.00, -0.25, -0.10" height="2.75" direction="1" />
      <CapsuleCollider boneName="cf_J_Kosi02_s" radius="1.30" center="0.00, 0.00, -0.13" height="3.00" direction="0" />
      
      <SphereColliderPair>
        <first boneName="cf_J_Mune01_s_L" radius="0.50" center="0.00, -0.10, -0.05" />
        <second boneName="cf_J_Mune_Nip01_s_L" radius="0.65" center="0.00, 0.00, -0.65" />
      </SphereColliderPair>
      <SphereColliderPair>
        <first boneName="cf_J_Mune01_s_R" radius="0.50" center="0.00, -0.10, -0.05" />
        <second boneName="cf_J_Mune_Nip01_s_R" radius="0.65" center="0.00, 0.00, -0.65" />
      </SphereColliderPair>

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

    # 안전하게 category 읽기
    category = (manifest_cloth.attrib.get("category") or "").lower()

    template_xml = None

    if "_top" in category:
        template_xml = TEMPLATE_XML_TOP
        print(f"[REPLACE-TOP] cloth category='{category}'")

    elif "_bot" in category:
        template_xml = TEMPLATE_XML_BOTTOM
        print(f"[REPLACE-BOTTOM] cloth category='{category}'")

    else:
        print(f"[SKIP] cloth category='{category}'")
        return False

    # template 파싱
    template_root = ET.fromstring(template_xml)

    # 기존 collider 제거
    for node in list(manifest_cloth):
        if node.tag in ("CapsuleCollider", "SphereColliderPair"):
            manifest_cloth.remove(node)

    # TEMPLATE collider 삽입
    for child in template_root:
        manifest_cloth.append(copy.deepcopy(child))

    return True


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
def process(zipmod_path):

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

    # Repack
    output_zip = os.path.join(base_dir, f"{name}_patched.zipmod")
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

    parser.add_argument("zipmod", help="Input zip or zipmod file")

    args = parser.parse_args()

    process(args.zipmod)