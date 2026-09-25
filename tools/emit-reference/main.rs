// Referenz-Harness: fuettert die ORIGINALEN emit.rs/uci.rs (lila-engine) mit Testfaellen und gibt je
// Eingabezeile aus, was lila-engine an den Anfragenden schicken wuerde. Abweichend vom Broker laeuft der
// Harness nach einer Fehlerzeile WEITER (Zustand bleibt unberuehrt, wie bei lila-engine vor dem Abbruch).
mod emit;
mod model;
mod uci;

use serde::Deserialize;
use shakmaty::{fen::Fen, uci::UciMove, variant::{Variant, VariantPosition}, CastlingMode, Position};

#[derive(Deserialize)]
struct Case { name: String, fen: String, moves: Vec<String>, lines: Vec<String> }

fn main() {
    let path = std::env::args().nth(1).expect("cases.json");
    let cases: Vec<Case> = serde_json::from_str(&std::fs::read_to_string(path).unwrap()).unwrap();
    let mut out = serde_json::Map::new();
    for case in cases {
        let fen: Fen = case.fen.parse().expect("fen");
        let mut pos = VariantPosition::from_setup(Variant::Chess, fen.into_setup(), CastlingMode::Chess960).expect("pos");
        let mut work_moves = Vec::new();
        for m in &case.moves {
            let uci: UciMove = m.parse().expect("uci");
            let mv = uci.to_move(&pos).expect("legal");
            work_moves.push(mv.to_uci(CastlingMode::Chess960).to_string());
            pos.play_unchecked(mv);
        }
        let mut emit = emit::Emit::default();
        let mut results: Vec<serde_json::Value> = Vec::new();
        let mut finished = false;
        for line in &case.lines {
            if line.trim_start().starts_with('{') { results.push(serde_json::Value::String(format!("CONTROL:{}", line))); continue; }
            match uci::UciOut::from_line(line) {
                Ok(Some(uci::UciOut::Bestmove { m, ponder })) => {
                    emit.finish(m, ponder);
                    results.push(serde_json::Value::String(serde_json::to_string(&emit).unwrap()));
                    finished = true;
                    break;
                }
                Ok(Some(u)) => {
                    emit.update(&u, &pos);
                    if emit.should_emit() { results.push(serde_json::Value::String(serde_json::to_string(&emit).unwrap())); }
                    else { results.push(serde_json::Value::Null); }
                }
                Ok(None) => results.push(serde_json::Value::String("IGNORED".into())),
                Err(e) => results.push(serde_json::Value::String(format!("ERR:{}", e))),
            }
        }
        if !finished {
            emit.finish(uci::BestMove::default(), None);
            results.push(serde_json::Value::String(format!("EOF:{}", serde_json::to_string(&emit).unwrap())));
        }
        let mut o = serde_json::Map::new();
        o.insert("moves".into(), serde_json::json!(work_moves));
        o.insert("results".into(), serde_json::Value::Array(results));
        out.insert(case.name, serde_json::Value::Object(o));
    }
    println!("{}", serde_json::to_string_pretty(&serde_json::Value::Object(out)).unwrap());
}
