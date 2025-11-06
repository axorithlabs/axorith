#!/bin/bash
# Axorith Test Runner for Linux/macOS

set -e

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Default values
FILTER=""
COVERAGE=false
OPEN_REPORT=false
VERBOSE=false
PROJECT="All"
CLEAN=false

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --filter)
            FILTER="$2"
            shift 2
            ;;
        --coverage)
            COVERAGE=true
            shift
            ;;
        --open)
            OPEN_REPORT=true
            shift
            ;;
        --verbose)
            VERBOSE=true
            shift
            ;;
        --project)
            PROJECT="$2"
            shift 2
            ;;
        --clean)
            CLEAN=true
            shift
            ;;
        --help)
            echo "Usage: ./run-tests.sh [options]"
            echo ""
            echo "Options:"
            echo "  --filter <pattern>    Filter tests by name"
            echo "  --coverage            Generate coverage report"
            echo "  --open                Open coverage report in browser"
            echo "  --verbose             Verbose output"
            echo "  --project <name>      Test specific project (Sdk, Core, Shared, All)"
            echo "  --clean               Clean before running"
            echo "  --help                Show this help"
            exit 0
            ;;
        *)
            echo "Unknown option: $1"
            echo "Use --help for usage information"
            exit 1
            ;;
    esac
done

echo -e "${CYAN}🧪 Axorith Test Runner${NC}"
echo -e "${CYAN}=====================${NC}\n"

# Get script directory
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"
SOLUTION_PATH="$ROOT_DIR/Axorith.sln"

# Clean if requested
if [ "$CLEAN" = true ]; then
    echo -e "${YELLOW}🧹 Cleaning solution...${NC}"
    dotnet clean "$SOLUTION_PATH" --verbosity quiet
    echo -e "${GREEN}✅ Cleaned${NC}\n"
fi

# Build test arguments
TEST_ARGS="--verbosity normal"

if [ "$VERBOSE" = true ]; then
    TEST_ARGS="--verbosity detailed"
fi

if [ -n "$FILTER" ]; then
    TEST_ARGS="$TEST_ARGS --filter $FILTER"
    echo -e "${CYAN}🔍 Filter: $FILTER${NC}"
fi

# Determine test target
case $PROJECT in
    Sdk)
        TEST_TARGET="$ROOT_DIR/tests/Axorith.Sdk.Tests/Axorith.Sdk.Tests.csproj"
        echo -e "${CYAN}📦 Testing: Axorith.Sdk.Tests${NC}"
        ;;
    Core)
        TEST_TARGET="$ROOT_DIR/tests/Axorith.Core.Tests/Axorith.Core.Tests.csproj"
        echo -e "${CYAN}📦 Testing: Axorith.Core.Tests${NC}"
        ;;
    Shared)
        TEST_TARGET="$ROOT_DIR/tests/Axorith.Shared.Tests/Axorith.Shared.Tests.csproj"
        echo -e "${CYAN}📦 Testing: Axorith.Shared.Tests${NC}"
        ;;
    All|*)
        TEST_TARGET="$SOLUTION_PATH"
        echo -e "${CYAN}📦 Testing: All projects${NC}"
        ;;
esac

# Coverage setup
COVERAGE_DIR="$ROOT_DIR/coverage"
REPORT_DIR="$ROOT_DIR/coverage-report"

if [ "$COVERAGE" = true ]; then
    echo -e "${CYAN}📊 Coverage: Enabled${NC}"
    TEST_ARGS="$TEST_ARGS --collect:XPlat Code Coverage --results-directory $COVERAGE_DIR"
    
    # Clean old coverage
    if [ -d "$COVERAGE_DIR" ]; then
        rm -rf "$COVERAGE_DIR"
    fi
fi

echo -e "\n${YELLOW}⚡ Running tests...${NC}\n"

# Run tests
if dotnet test "$TEST_TARGET" $TEST_ARGS; then
    echo -e "\n${GREEN}✅ All tests passed!${NC}"
    
    # Generate coverage report
    if [ "$COVERAGE" = true ]; then
        echo -e "\n${YELLOW}📈 Generating coverage report...${NC}"
        
        # Check if reportgenerator is installed
        if ! command -v reportgenerator &> /dev/null; then
            echo -e "${YELLOW}Installing ReportGenerator...${NC}"
            dotnet tool install -g dotnet-reportgenerator-globaltool
        fi
        
        # Generate report
        reportgenerator \
            -reports:"$COVERAGE_DIR/**/coverage.cobertura.xml" \
            -targetdir:"$REPORT_DIR" \
            -reporttypes:"Html;Badges;Cobertura" \
            -verbosity:Warning
        
        echo -e "${GREEN}✅ Coverage report generated: $REPORT_DIR/index.html${NC}"
        
        # Open report
        if [ "$OPEN_REPORT" = true ]; then
            echo -e "${CYAN}🌐 Opening report in browser...${NC}"
            if command -v xdg-open &> /dev/null; then
                xdg-open "$REPORT_DIR/index.html"
            elif command -v open &> /dev/null; then
                open "$REPORT_DIR/index.html"
            else
                echo -e "${YELLOW}⚠️ Cannot open browser automatically${NC}"
            fi
        fi
    fi
    
    echo -e "\n${GREEN}🎉 Done!${NC}"
else
    echo -e "\n${RED}❌ Tests failed!${NC}"
    exit 1
fi
