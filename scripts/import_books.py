#!/usr/bin/env python3
"""
Import book puzzles from schach-bot PGN files into RookHub.

Usage:
    python import_books.py [--books-dir PATH] [--api-url URL] [--token JWT]

Requires: python-chess, requests
    pip install python-chess requests
"""

import argparse
import json
import os
import sys
from pathlib import Path

import chess.pgn
import requests


def parse_pgn_file(pgn_path: Path, book_meta: dict) -> list[dict]:
    """Parse a PGN file and return a list of puzzle dicts."""
    filename = pgn_path.name
    meta = book_meta.get(filename, {})
    difficulty = meta.get("difficulty")
    book_rating = meta.get("rating")
    tags_list = meta.get("tags", [])
    tags = " ".join(tags_list) if tags_list else None

    puzzles = []
    skipped = 0

    with open(pgn_path, encoding="utf-8", errors="replace") as f:
        while True:
            game = chess.pgn.read_game(f)
            if game is None:
                break

            headers = game.headers
            fen = headers.get("FEN", "")
            round_val = headers.get("Round", "")
            white = headers.get("White", "")
            black = headers.get("Black", "")

            # Skip entries without FEN (intro pages, score charts, etc.)
            if not fen or fen == "?":
                skipped += 1
                continue

            if not round_val or round_val == "?":
                skipped += 1
                continue

            # Build line_id
            line_id = f"{filename}:{round_val}"

            # Extract mainline moves as UCI
            board = game.board()
            uci_moves = []
            node = game
            while node.variations:
                next_node = node.variation(0)
                move = next_node.move
                uci_moves.append(move.uci())
                board.push(move)
                node = next_node

            if not uci_moves:
                skipped += 1
                continue

            # Extract first comment from mainline
            comment = None
            node = game
            while node.variations:
                next_node = node.variation(0)
                if next_node.comment:
                    # Clean up Chessbase annotations
                    raw = next_node.comment
                    # Remove [%tqu ...], [%cal ...], [%csl ...] annotations
                    import re
                    cleaned = re.sub(r'\[%\w+[^\]]*\]', '', raw).strip()
                    if cleaned:
                        comment = cleaned[:5000]
                    break
                node = next_node

            puzzles.append({
                "lineId": line_id,
                "bookFileName": filename,
                "round": round_val[:20],
                "fen": fen,
                "moves": " ".join(uci_moves),
                "title": white[:300] if white else None,
                "chapter": black[:200] if black else None,
                "comment": comment,
                "difficulty": difficulty,
                "bookRating": book_rating,
                "tags": tags,
            })

    return puzzles, skipped


def main():
    parser = argparse.ArgumentParser(description="Import book puzzles into RookHub")
    parser.add_argument("--books-dir", default="../schach-bot/books",
                        help="Path to schach-bot books directory")
    parser.add_argument("--api-url", default="http://localhost:5001",
                        help="RookHub API base URL")
    parser.add_argument("--token", required=True,
                        help="JWT token for admin authentication")
    parser.add_argument("--batch-size", type=int, default=500,
                        help="Number of puzzles per API request")
    parser.add_argument("--dry-run", action="store_true",
                        help="Parse only, don't send to API")
    parser.add_argument("--mapping-file", default="scripts/book_puzzle_mapping.json",
                        help="Output mapping file path")
    args = parser.parse_args()

    books_dir = Path(args.books_dir).resolve()
    if not books_dir.exists():
        print(f"Error: Books directory not found: {books_dir}")
        sys.exit(1)

    # Load books.json metadata
    books_json_path = books_dir / "books.json"
    book_meta = {}
    if books_json_path.exists():
        with open(books_json_path, encoding="utf-8") as f:
            book_meta = json.load(f)
        print(f"Loaded metadata for {len(book_meta)} books")
    else:
        print("Warning: books.json not found, importing without metadata")

    # Parse all PGN files
    pgn_files = sorted(books_dir.glob("*.pgn"))
    print(f"Found {len(pgn_files)} PGN files")

    all_puzzles = []
    total_skipped = 0

    for pgn_path in pgn_files:
        print(f"  Parsing {pgn_path.name}...", end=" ", flush=True)
        puzzles, skipped = parse_pgn_file(pgn_path, book_meta)
        all_puzzles.extend(puzzles)
        total_skipped += skipped
        print(f"{len(puzzles)} puzzles, {skipped} skipped")

    print(f"\nTotal: {len(all_puzzles)} puzzles, {total_skipped} skipped")

    if args.dry_run:
        print("Dry run — not sending to API")
        return

    # Send to API in batches
    headers = {
        "Authorization": f"Bearer {args.token}",
        "Content-Type": "application/json"
    }
    api_url = f"{args.api_url}/api/admin/book-puzzles/import"

    total_imported = 0
    total_api_skipped = 0
    mapping = {}

    for i in range(0, len(all_puzzles), args.batch_size):
        batch = all_puzzles[i:i + args.batch_size]
        print(f"  Sending batch {i // args.batch_size + 1} ({len(batch)} puzzles)...", end=" ", flush=True)

        resp = requests.post(api_url, json=batch, headers=headers, timeout=120)
        if resp.status_code != 200:
            print(f"FAILED: {resp.status_code} {resp.text}")
            sys.exit(1)

        result = resp.json()
        total_imported += result.get("imported", 0)
        total_api_skipped += result.get("skipped", 0)
        print(f"imported={result.get('imported', 0)}, skipped={result.get('skipped', 0)}")

    print(f"\nDone! Imported: {total_imported}, Skipped: {total_api_skipped}")

    # Build mapping by looking up line_ids
    print("Building mapping file...")
    for puzzle in all_puzzles:
        line_id = puzzle["lineId"]
        resp = requests.get(
            f"{args.api_url}/api/book-puzzles/by-line-id",
            params={"lineId": line_id},
            headers=headers,
            timeout=30
        )
        if resp.status_code == 200:
            mapping[line_id] = resp.json().get("id")

    mapping_path = Path(args.mapping_file)
    mapping_path.parent.mkdir(parents=True, exist_ok=True)
    with open(mapping_path, "w", encoding="utf-8") as f:
        json.dump(mapping, f, indent=2)
    print(f"Mapping saved to {mapping_path} ({len(mapping)} entries)")


if __name__ == "__main__":
    main()
