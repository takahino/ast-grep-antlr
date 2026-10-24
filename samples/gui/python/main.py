#!/usr/bin/env python3
"""Simple tkinter GUI that loads ag4_native via ctypes."""

from __future__ import annotations

import json
import sys
import tkinter as tk
from tkinter import messagebox, ttk

from ag4_native import Ag4Native, default_fixtures_dir, resolve_lib_dir


def smoke() -> int:
    try:
        api = Ag4Native()
        print(f"version={api.version()}")
        rc, body = api.search(
            {
                "pattern": "foo($$$ARGS)",
                "language": "java",
                "paths": [str(default_fixtures_dir())],
                "maxResults": 10,
            }
        )
        print(f"paths rc={rc}")
        print(body)
        if rc != 0:
            return 1
        matches = json.loads(body)
        print(f"paths matches={len(matches)}")
        if not matches:
            return 2

        src_rc, src_body = api.search(
            {
                "pattern": "foo($$$ARGS)",
                "language": "java",
                "sources": [
                    {
                        "path": "snippet.java",
                        "content": "class Demo { void m() { foo(1, 2); } }\n",
                    }
                ],
                "maxResults": 10,
            }
        )
        print(f"sources rc={src_rc}")
        print(src_body)
        if src_rc != 0:
            return 1
        src_matches = json.loads(src_body)
        print(f"sources matches={len(src_matches)}")
        return 0 if src_matches else 2
    except Exception as ex:  # noqa: BLE001 — sample CLI
        print(ex, file=sys.stderr)
        return 3


class App(tk.Tk):
    def __init__(self) -> None:
        super().__init__()
        self.title("ag4 sample GUI (Python / ctypes)")
        self.geometry("900x560")
        self.api = Ag4Native()

        frm = ttk.Frame(self, padding=8)
        frm.pack(fill=tk.X)

        ttk.Label(frm, text="Language").grid(row=0, column=0, sticky=tk.W)
        self.lang = ttk.Combobox(frm, values=["java", "csharp"], state="readonly")
        self.lang.current(0)
        self.lang.grid(row=0, column=1, sticky=tk.EW, padx=4, pady=2)

        ttk.Label(frm, text="Pattern").grid(row=1, column=0, sticky=tk.W)
        self.pattern = ttk.Entry(frm)
        self.pattern.insert(0, "foo($$$ARGS)")
        self.pattern.grid(row=1, column=1, sticky=tk.EW, padx=4, pady=2)

        ttk.Label(frm, text="Path").grid(row=2, column=0, sticky=tk.W)
        self.path = ttk.Entry(frm)
        self.path.insert(0, str(default_fixtures_dir()))
        self.path.grid(row=2, column=1, sticky=tk.EW, padx=4, pady=2)

        ttk.Button(frm, text="Search", command=self.run_search).grid(row=3, column=1, sticky=tk.W, pady=4)
        frm.columnconfigure(1, weight=1)

        cols = ("file", "line", "col", "text")
        self.tree = ttk.Treeview(self, columns=cols, show="headings")
        for c, w in zip(cols, (280, 60, 60, 420), strict=True):
            self.tree.heading(c, text=c.capitalize())
            self.tree.column(c, width=w, stretch=c == "text")
        self.tree.pack(fill=tk.BOTH, expand=True, padx=8, pady=4)

        self.status = ttk.Label(self, text=f"ag4_native {self.api.version()}  lib={resolve_lib_dir()}")
        self.status.pack(fill=tk.X, padx=8, pady=4)

    def run_search(self) -> None:
        for item in self.tree.get_children():
            self.tree.delete(item)
        try:
            rc, body = self.api.search(
                {
                    "pattern": self.pattern.get(),
                    "language": self.lang.get(),
                    "paths": [self.path.get()],
                    "strictness": "smart",
                    "maxResults": 200,
                }
            )
            if rc != 0:
                err = json.loads(body) if body else {}
                msg = err.get("error", body)
                self.status.configure(text=f"Error ({rc}): {msg}")
                return
            rows = json.loads(body)
            for m in rows:
                start = m["range"]["start"]
                self.tree.insert(
                    "",
                    tk.END,
                    values=(m.get("file", ""), start["line"] + 1, start["column"] + 1, m.get("text", "")),
                )
            self.status.configure(text=f"{len(rows)} match(es)")
        except Exception as ex:  # noqa: BLE001 — sample UI
            self.status.configure(text=str(ex))
            messagebox.showerror("ag4", str(ex))


if __name__ == "__main__":
    if len(sys.argv) == 2 and sys.argv[1] == "--smoke":
        raise SystemExit(smoke())
    App().mainloop()
