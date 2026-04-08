import os
import xml.etree.ElementTree as ET
from pathlib import Path


NAMESPACE = {"trx": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
CATEGORIES = [
    ("Integration Tests", "GameServer.Tests.Integration."),
    ("System Tests", "GameServer.Tests.Systems."),
]
FAILED_OUTCOMES = {"Failed", "Error", "Timeout", "Aborted"}


def escape_markdown(value: str) -> str:
    return value.replace("|", "\\|").replace("\n", " ").strip()


def classify_test(class_name: str) -> str | None:
    for title, namespace_prefix in CATEGORIES:
        if class_name.startswith(namespace_prefix):
            return title
    return None


def normalize_outcome(outcome: str) -> str:
    if outcome == "Passed":
        return "Passed"
    if outcome in FAILED_OUTCOMES:
        return "Failed"
    return "Skipped"


def format_duration(duration: str) -> str:
    if not duration:
        return "-"

    hours, minutes, seconds = duration.split(":")
    total_seconds = int(hours) * 3600 + int(minutes) * 60 + float(seconds)
    return f"{total_seconds:.2f}s"


def read_test_results(trx_files: list[Path]) -> dict[str, list[dict[str, str]]]:
    categorized_results = {title: [] for title, _ in CATEGORIES}

    for trx_file in trx_files:
        root = ET.parse(trx_file).getroot()
        test_metadata = {}

        for unit_test in root.findall(".//trx:UnitTest", NAMESPACE):
            test_method = unit_test.find("trx:TestMethod", NAMESPACE)
            if test_method is None:
                continue

            test_metadata[unit_test.attrib["id"]] = {
                "class_name": test_method.attrib.get("className", ""),
                "name": unit_test.attrib.get("name", ""),
            }

        for result in root.findall(".//trx:UnitTestResult", NAMESPACE):
            test_id = result.attrib.get("testId", "")
            metadata = test_metadata.get(test_id)
            if metadata is None:
                continue

            category = classify_test(metadata["class_name"])
            if category is None:
                continue

            message = result.findtext("trx:Output/trx:ErrorInfo/trx:Message", default="", namespaces=NAMESPACE)
            categorized_results[category].append({
                "name": result.attrib.get("testName", metadata["name"]),
                "outcome": normalize_outcome(result.attrib.get("outcome", "")),
                "duration": format_duration(result.attrib.get("duration", "")),
                "message": message.splitlines()[0].strip() if message else "",
            })

    return categorized_results


def render_category(title: str, tests: list[dict[str, str]]) -> list[str]:
    lines = ["", f"## {title}"]

    if not tests:
        lines.append("No matching tests were found in the `.trx` results.")
        return lines

    total = len(tests)
    passed = sum(1 for test in tests if test["outcome"] == "Passed")
    failed = sum(1 for test in tests if test["outcome"] == "Failed")
    skipped = total - passed - failed
    pass_rate = 0.0 if total == 0 else passed / total * 100

    lines.extend([
        "| Total | Passed | Failed | Skipped | Pass rate |",
        "| --- | --- | --- | --- | --- |",
        f"| {total} | {passed} | {failed} | {skipped} | {pass_rate:.1f}% |",
        "",
        "<details>",
        f"<summary>Show {title.lower()}</summary>",
        "",
        "| Result | Test | Duration | Notes |",
        "| --- | --- | --- | --- |",
    ])

    for test in sorted(tests, key=lambda item: (item["outcome"], item["name"])):
        note = escape_markdown(test["message"]) if test["message"] else "-"
        lines.append(f"| {test['outcome']} | {escape_markdown(test['name'])} | {test['duration']} | {note} |")

    lines.extend(["", "</details>"])
    return lines


def main() -> None:
    summary_path = Path(os.environ["GITHUB_STEP_SUMMARY"])
    trx_files = sorted(Path("TestResults").rglob("*.trx"))
    categorized_results = read_test_results(trx_files)

    lines = []
    if not trx_files:
        lines.extend([
            "",
            "## API Test Suites",
            "No `.trx` files were found in `TestResults`.",
        ])
    else:
        for title, _ in CATEGORIES:
            lines.extend(render_category(title, categorized_results[title]))

    with summary_path.open("a", encoding="utf-8") as summary:
        summary.write("\n".join(lines) + "\n")


if __name__ == "__main__":
    main()
