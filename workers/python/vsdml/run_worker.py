import time

from vsdml.health import ping

if __name__ == "__main__":
    print("Worker starting (stub)...")
    print("health:", ping())
    print("backend:", "CPU (probe to be added by Agent D)")
    time.sleep(0.5)
    print("Worker exiting (stub).")
