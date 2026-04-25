import zipfile
import os
import argparse
import xml.etree.ElementTree as ET


# ----------------------------------
# Check if zip contains cloth tag
# ----------------------------------
def has_cloth_tag(zip_path):

    try:
        with zipfile.ZipFile(zip_path, 'r') as z:

            # Find manifest.xml inside archive
            manifest_name = None
            for name in z.namelist():
                if name.lower().endswith("manifest.xml"):
                    manifest_name = name
                    break

            if manifest_name is None:
                return False

            # Read manifest.xml directly from zip
            with z.open(manifest_name) as f:
                tree = ET.parse(f)
                root = tree.getroot()

                cloth = root.find(".//cloth")
                return cloth is not None

    except Exception as e:
        print(f"[ERROR] Failed to read {zip_path} : {e}")
        return False


# ----------------------------------
# Scan folder
# ----------------------------------
def scan_folder(folder):

    results = []

    for root, dirs, files in os.walk(folder):
        for file in files:

            if not file.lower().endswith((".zipmod", ".zip")):
                continue

            full_path = os.path.join(root, file)

            print(f"Checking: {file}")

            if has_cloth_tag(full_path):
                results.append(full_path)

    return results


# ----------------------------------
# Main
# ----------------------------------
if __name__ == "__main__":

    parser = argparse.ArgumentParser(description="Find zip/zipmod containing cloth tag")
    parser.add_argument("folder", help="Target folder path")
    args = parser.parse_args()

    matches = scan_folder(args.folder)

    print("\n===== Physical Clothes Found =====")

    for m in matches:
        print(m)

    print(f"\nTotal: {len(matches)} files")
