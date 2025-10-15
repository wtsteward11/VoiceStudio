import argparse
import os


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--in", dest="inp", required=True)
    ap.add_argument("--out", dest="out", required=True)
    ap.add_argument("--model")
    ap.add_argument("--index")
    ap.add_argument("--key", type=int, default=0)
    ap.add_argument("--f0", choices=["on", "off"], default="on")
    args = ap.parse_args()

    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    with open(args.inp, "rb") as s, open(args.out, "wb") as d:
        d.write(s.read())
    print("RVC stub: copied input to output. Replace with real RVC engine.")


if __name__ == "__main__":
    main()
