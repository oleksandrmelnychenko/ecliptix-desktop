#!/usr/bin/env python3
"""
Ecliptix Desktop Version Helper

This script helps manage versioning for Ecliptix Desktop builds across all project files.
It can increment versions, create build numbers, and update MSBuild properties.

Usage:
  python version-helper.py --action [increment|build|current|set] [options]

Examples:
  python version-helper.py --action current                    # Show current version
  python version-helper.py --action increment --part patch     # Increment patch version (0.0.1 -> 0.0.2)
  python version-helper.py --action increment --part minor     # Increment minor version (0.0.1 -> 0.1.0)
  python version-helper.py --action increment --part major     # Increment major version (0.0.1 -> 1.0.0)
  python version-helper.py --action build                      # Generate build number using timestamp
  python version-helper.py --action set --version "1.2.3"     # Set specific version
"""

import argparse
import os
import re
import xml.etree.ElementTree as ET
from datetime import datetime
from pathlib import Path
from typing import List, Tuple, Optional
import json


class VersionHelper:
    def __init__(self, root_dir: str = None):
        self.root_dir = Path(root_dir) if root_dir else Path(__file__).parent.parent
        self.project_files = []
        self._find_project_files()
    
    def _find_project_files(self):
        """Find all .csproj files in the solution"""
        for csproj in self.root_dir.rglob("*.csproj"):
            if "bin" not in str(csproj) and "obj" not in str(csproj):
                self.project_files.append(csproj)
        
        print(f"Found {len(self.project_files)} project files:")
        for proj in self.project_files:
            print(f"  - {proj.relative_to(self.root_dir)}")
    
    def get_current_version(self) -> str:
        """Get current version from Directory.Build.props"""
        directory_build_props = self.root_dir / "Ecliptix.Core" / "Directory.Build.props"
        if not directory_build_props.exists():
            return "0.1.0"
        
        try:
            tree = ET.parse(directory_build_props)
            root = tree.getroot()
            
            major = minor = patch = "0"
            
            for property_group in root.findall("PropertyGroup"):
                major_elem = property_group.find("MajorVersion")
                minor_elem = property_group.find("MinorVersion") 
                patch_elem = property_group.find("PatchVersion")
                
                if major_elem is not None and major_elem.text:
                    major = major_elem.text
                if minor_elem is not None and minor_elem.text:
                    minor = minor_elem.text
                if patch_elem is not None and patch_elem.text:
                    patch = patch_elem.text
            
            return f"{major}.{minor}.{patch}"
        except Exception as e:
            print(f"Error reading version: {e}")
            return "0.1.0"
    
    def _get_main_project(self) -> Optional[Path]:
        """Get the main UI project file"""
        for proj in self.project_files:
            if "Ecliptix.Core.csproj" in proj.name:
                return proj
        return self.project_files[0] if self.project_files else None
    
    def increment_version(self, part: str) -> str:
        """Increment version part (major, minor, patch)"""
        current = self.get_current_version()
        parts = current.split('.')
        
        # Ensure we have at least 3 parts
        while len(parts) < 3:
            parts.append('0')
        
        major, minor, patch = int(parts[0]), int(parts[1]), int(parts[2])
        
        if part == "major":
            major += 1
            minor = 0
            patch = 0
        elif part == "minor":
            minor += 1
            patch = 0
        elif part == "patch":
            patch += 1
        else:
            raise ValueError(f"Invalid version part: {part}. Use 'major', 'minor', or 'patch'")
        
        new_version = f"{major}.{minor}.{patch}"
        self.set_version(new_version)
        return new_version
    
    def set_version(self, version: str):
        """Set version in Directory.Build.props"""
        # Validate version format
        if not re.match(r'^\d+\.\d+\.\d+$', version):
            raise ValueError(f"Invalid version format: {version}. Use x.y.z format")
        
        directory_build_props = self.root_dir / "Ecliptix.Core" / "Directory.Build.props"
        if not directory_build_props.exists():
            raise FileNotFoundError(f"Directory.Build.props not found at {directory_build_props}")
        
        parts = version.split('.')
        major, minor, patch = parts[0], parts[1], parts[2]
        
        try:
            # Read file content
            with open(directory_build_props, 'r', encoding='utf-8') as f:
                content = f.read()
            
            # Update version components
            content = re.sub(r'<MajorVersion>\d+</MajorVersion>', f'<MajorVersion>{major}</MajorVersion>', content)
            content = re.sub(r'<MinorVersion>\d+</MinorVersion>', f'<MinorVersion>{minor}</MinorVersion>', content)
            content = re.sub(r'<PatchVersion>\d+</PatchVersion>', f'<PatchVersion>{patch}</PatchVersion>', content)
            
            # Write back to file
            with open(directory_build_props, 'w', encoding='utf-8') as f:
                f.write(content)
            
            print(f"Updated version to {version} in Directory.Build.props")
                
        except Exception as e:
            print(f"Error updating Directory.Build.props: {e}")
    
    def _add_assembly_version_to_project(self, content: str, version: str) -> str:
        """Add AssemblyVersion to project file if it doesn't exist"""
        # Find the first PropertyGroup
        pattern = r'(<PropertyGroup>)'
        
        def replacement(match):
            return f'{match.group(1)}\n    <AssemblyVersion>{version}</AssemblyVersion>'
        
        return re.sub(pattern, replacement, content, count=1)
    
    def generate_build_number(self) -> str:
        """Generate build number using timestamp"""
        now = datetime.now()
        # Format: YYMMDD.HHMM (e.g., 240315.1430)
        return f"{now.strftime('%y%m%d.%H%M')}"
    
    def create_build_info(self) -> dict:
        """Create build information dictionary"""
        current_version = self.get_current_version()
        build_number = self.generate_build_number()
        
        build_info = {
            "version": current_version,
            "build_number": build_number,
            "full_version": f"{current_version}-build.{build_number}",
            "timestamp": datetime.now().isoformat(),
            "git_commit": self._get_git_commit(),
            "git_branch": self._get_git_branch()
        }
        
        # Write to build-info.json
        build_info_file = self.root_dir / "build-info.json"
        with open(build_info_file, 'w') as f:
            json.dump(build_info, f, indent=2)
        
        print(f"Created build info: {build_info_file}")
        return build_info
    
    def _get_git_commit(self) -> str:
        """Get current git commit hash"""
        try:
            import subprocess
            result = subprocess.run(['git', 'rev-parse', 'HEAD'], 
                                  capture_output=True, text=True, cwd=self.root_dir)
            return result.stdout.strip()[:8] if result.returncode == 0 else "unknown"
        except:
            return "unknown"
    
    def _get_git_branch(self) -> str:
        """Get current git branch"""
        try:
            import subprocess
            result = subprocess.run(['git', 'rev-parse', '--abbrev-ref', 'HEAD'], 
                                  capture_output=True, text=True, cwd=self.root_dir)
            return result.stdout.strip() if result.returncode == 0 else "unknown"
        except:
            return "unknown"


def main():
    parser = argparse.ArgumentParser(description='Ecliptix Desktop Version Helper')
    parser.add_argument('--action', required=True, 
                       choices=['increment', 'build', 'current', 'set'],
                       help='Action to perform')
    parser.add_argument('--part', choices=['major', 'minor', 'patch'],
                       help='Version part to increment (for increment action)')
    parser.add_argument('--version', 
                       help='Specific version to set (for set action)')
    parser.add_argument('--root', 
                       help='Root directory of the project')
    
    args = parser.parse_args()
    
    try:
        helper = VersionHelper(args.root)
        
        if args.action == 'current':
            current = helper.get_current_version()
            print(f"Current version: {current}")
            
        elif args.action == 'increment':
            if not args.part:
                print("Error: --part is required for increment action")
                return 1
            new_version = helper.increment_version(args.part)
            print(f"Version incremented to: {new_version}")
            
        elif args.action == 'set':
            if not args.version:
                print("Error: --version is required for set action")
                return 1
            helper.set_version(args.version)
            print(f"Version set to: {args.version}")
            
        elif args.action == 'build':
            build_info = helper.create_build_info()
            print(f"Build created: {build_info['full_version']}")
            print(f"Git: {build_info['git_commit']} on {build_info['git_branch']}")
        
        return 0
        
    except Exception as e:
        print(f"Error: {e}")
        return 1


if __name__ == '__main__':
    exit(main())