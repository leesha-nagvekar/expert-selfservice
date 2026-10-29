"""Add every Danish postcode to data/lookups/Cities.csv and Zipcodes.csv.

One-off tool, not part of the reset. Source: the GeoNames postcode file for
Denmark (public data, CC BY 4.0, https://download.geonames.org/export/zip/).

Rows with Id below FIRST_DANISH_ID (the rows from VBA's lookup export) are kept
as they are. Rows from FIRST_DANISH_ID up are replaced on every run.

Run: python tools/build_postcodes.py
"""

import csv
import io
import urllib.request
import zipfile
from pathlib import Path

SOURCE_URL = "https://download.geonames.org/export/zip/DK.zip"
LOOKUPS = Path(__file__).resolve().parent.parent / "data" / "lookups"
FIRST_DANISH_ID = 2001

# GeoNames admin1 code -> Regions.Id in data/lookups/Regions.csv.
# Codes 81-85 ("Region ...") are only used for PO boxes and bulk-mail
# postcodes, which experts don't live at, so they are left out.
REGION_BY_ADMIN1 = {
    "17": 7,  # Capital Region -> Hovedstaden
    "18": 4,  # Central Jutland -> Midtjylland
    "19": 5,  # North Denmark -> Nordjylland
    "20": 8,  # Zealand -> Sjælland
    "21": 6,  # South Denmark -> Syddanmark
}


def fetch_danish_postcodes() -> list[tuple[str, str, int]]:
    """Return (postcode, city, region_id), sorted by postcode."""
    with urllib.request.urlopen(SOURCE_URL, timeout=60) as response:
        archive = zipfile.ZipFile(io.BytesIO(response.read()))
    text = archive.read("DK.txt").decode("utf-8")

    rows = {}
    for line in text.splitlines():
        fields = line.split("\t")
        postcode, city, admin1 = fields[1], fields[2], fields[4]
        if admin1 not in REGION_BY_ADMIN1 or postcode.startswith("0"):
            continue
        rows[postcode] = (postcode, city, REGION_BY_ADMIN1[admin1])
    return [rows[key] for key in sorted(rows)]


def read_kept_rows(path: Path) -> tuple[list[str], list[list[str]]]:
    with path.open(encoding="utf-8-sig", newline="") as f:
        reader = csv.reader(f)
        header = next(reader)
        kept = [row for row in reader if int(row[0]) < FIRST_DANISH_ID]
    return header, kept


def write_rows(path: Path, header: list[str], rows: list[list]) -> None:
    with path.open("w", encoding="utf-8", newline="") as f:
        writer = csv.writer(f, lineterminator="\n")
        writer.writerow(header)
        writer.writerows(rows)


def main() -> None:
    postcodes = fetch_danish_postcodes()

    cities_path = LOOKUPS / "Cities.csv"
    zipcodes_path = LOOKUPS / "Zipcodes.csv"
    city_header, cities = read_kept_rows(cities_path)
    zip_header, zipcodes = read_kept_rows(zipcodes_path)

    for offset, (postcode, city, region_id) in enumerate(postcodes):
        row_id = FIRST_DANISH_ID + offset
        cities.append([row_id, city, region_id, postcode])
        zipcodes.append([row_id, row_id, postcode])

    write_rows(cities_path, city_header, cities)
    write_rows(zipcodes_path, zip_header, zipcodes)
    print(f"Wrote {len(postcodes)} Danish postcodes to {LOOKUPS}")


if __name__ == "__main__":
    main()
