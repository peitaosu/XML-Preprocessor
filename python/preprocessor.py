import os
import sys
import platform
import re
import subprocess
from lxml import etree

class Preprocessor():
    def __init__(self):
        self.original_file = {}
        self.processed_file = {}
        self.sys_vars = self.init_sys_vars()
        self.cus_vars = {}

    def load(self, file_path):
        self.original_file["file"] = file_path
        try:
            with open(file_path, "r") as original_file:
                self.original_file["content"] = original_file.read()
            return 0
        except:
            return -1

    def init_sys_vars(self):
        sys_vars = {}
        sys_vars["ARCH"] = platform.architecture()[0]
        sys_vars["SOURCE"] = os.path.abspath(__file__)
        sys_vars["CURRENT"] = os.getcwd()
        return sys_vars

    def parse_include(self, xml_str):
        include_regex = r"(<\?include([\w\s\\/.:]+)\s*\?>)"
        matches = re.findall(include_regex, xml_str)
        for group_inc, group_xml in matches:
            inc_file_path = group_xml.strip()
            with open(inc_file_path, "r") as inc_file:
                inc_file_content = inc_file.read()
                inc_file_content = self.parse_include(inc_file_content)
                xml_str = xml_str.replace(group_inc, inc_file_content)
        return xml_str

    def parse_env_var(self, xml_str):
        envvar_regex = r"(\$\(env\.(\w+)\))"
        matches = re.findall(envvar_regex, xml_str)
        for group_env, group_var in matches:
            xml_str = xml_str.replace(group_env, os.environ[group_var])
        return xml_str

    def parse_sys_var(self, xml_str):
        sysvar_regex = r"(\$\(sys\.(\w+)\))"
        matches = re.findall(sysvar_regex, xml_str)
        for group_sys, group_var in matches:
            if group_var not in self.sys_vars:
                raise Exception("Wrong System Variable: " + group_var)
            xml_str = xml_str.replace(group_sys, self.sys_vars[group_var])
        return xml_str

    def parse_cus_var(self, xml_str):
        define_regex = r"(<\?define\s*(\w+)\s*=\s*([\w\s\"]+)\s*\?>)"
        matches = re.findall(define_regex, xml_str)
        for group_def, group_name, group_var in matches:
            group_name = group_name.strip()
            group_var = group_var.strip().strip("\"")
            self.cus_vars[group_name] = group_var
            xml_str = xml_str.replace(group_def, "")
        cusvar_regex = r"(\$\(var\.(\w+)\))"
        matches = re.findall(cusvar_regex, xml_str)
        for group_cus, group_var in matches:
            if group_var not in self.cus_vars:
                continue
            xml_str = xml_str.replace(group_cus, self.cus_vars[group_var])
        return xml_str

    def parse_error_warning(self, xml_str):
        error_regex = r"(<\?error\s*\"([^\"]+)\"\s*\?>)"
        matches = re.findall(error_regex, xml_str)
        for group_err, group_var in matches:
            raise Exception("[Error]: " + group_var)
        warning_regex = r"(<\?warning\s*\"([^\"]+)\"\s*\?>)"
        matches = re.findall(warning_regex, xml_str)
        for group_wrn, group_var in matches:
            print "[Warning]: " + group_var
            xml_str = xml_str.replace(group_wrn, "")
        return xml_str

    def parse_if_elseif(self, xml_str):
        ifelif_regex = r"(<\?(if|elseif)\s*([^\"\s=<>!]+)\s*([!=<>]+)\s*\"*([^\"=<>!]+)\"*\s*\?>)"
        matches = re.findall(ifelif_regex, xml_str)
        for group_ifelif, group_tag, group_left, group_operator, group_right in matches:
            if "<" in group_operator or ">" in group_operator:
                result = eval(group_left + group_operator + group_right)
            else:
                result = eval('"{}" {} "{}"'.format(group_left, group_operator, group_right))
            xml_str = xml_str.replace(group_ifelif, "<?" + group_tag + " " + str(result) + "?>")
        return xml_str

    def parse_ifdef_ifndef(self, xml_str):
        ifndef_regex = r"(<\?(ifdef|ifndef)\s*([\w]+)\s*\?>)"
        matches = re.findall(ifndef_regex, xml_str)
        for group_ifndef, group_tag, group_var in matches:
            if group_tag == "ifdef":
                result = group_var in self.cus_vars
            else:
                result = group_var not in self.cus_vars
            xml_str = xml_str.replace(group_ifndef, "<?if " + str(result) + "?>")
        return xml_str

    def parse_foreach(self, xml_str):
        foreach_regex = r"((<\?foreach\s+(\w+)\s+in\s+([\w;]+)\s*\?>)((?!<\?endforeach\?>).*)(<\?endforeach\?>))"
        matches = re.findall(foreach_regex, xml_str)
        for group_for, group_forvars, group_name, group_vars, group_text, group_end in matches:
            group_texts = ""
            for var in group_vars.split(";"):
                self.cus_vars[group_name] = var
                group_texts += self.parse_cus_var(group_text)
            xml_str = xml_str.replace(group_for, group_texts)
        return xml_str

    def parse_if_else_if(self, xml_str):
        if_elif_else_regex = r"(<\?if\s(True|False)\?>\n(.*)\n<\?elseif\s(True|False)\?>\n(.*)\n<\?else\?>\n(.*)\n<\?endif\?>\n)"
        if_else_regex = r"(<\?if\s(True|False)\?>\n(.*)\n<\?else\?>\n(.*)\n<\?endif\?>\n)"
        if_regex = r"(<\?if\s(True|False)\?>\n(.*)\n<\?endif\?>\n)"
        matches = re.findall(if_elif_else_regex, xml_str)
        for group_full, group_if, group_if_elif, group_elif, group_elif_else, group_else in matches:
            result = ""
            if group_if == "True":
                result = group_if_elif
            elif group_elif == "True":
                result = group_elif_else
            else:
                result = group_else
            xml_str = xml_str.replace(group_full, result)
        matches = re.findall(if_else_regex, xml_str)
        for group_full, group_if, group_if_else, group_else in matches:
            result = ""
            if group_if == "True":
                result = group_if_else
            else:
                result = group_else
            xml_str = xml_str.replace(group_full, result)
        matches = re.findall(if_regex, xml_str)
        for group_full, group_if, group_text in matches:
            result = ""
            if group_if == "True":
                result = group_text
            xml_str = xml_str.replace(group_full, result)
        return xml_str

    def parse_command(self, xml_str):
        cmd_regex = r"(<\?cmd\s*\"([^\"]+)\"\s*\?>)"
        matches = re.findall(cmd_regex, xml_str)
        for group_cmd, group_exec in matches:
            try:
                output = subprocess.check_output(group_exec, shell=True, stderr=subprocess.STDOUT)
                print output
                xml_str = xml_str.replace(group_cmd, "")
            except subprocess.CalledProcessError as e:
                raise Exception(e.output)
        return xml_str

    def format_xml_str(self, xml_str):
        right_blank_regex = r"(>[\n\s\t\r]*)"
        left_blank_regex = r"([\n\s\t\r]*<)"
        matches = re.findall(right_blank_regex, xml_str)
        for group_full in matches:
            xml_str = xml_str.replace(group_full, ">")
        matches = re.findall(left_blank_regex, xml_str)
        for group_full in matches:
            xml_str = xml_str.replace(group_full, "<")
        xml_str.replace("\n", "")
        return xml_str

    def need_parse(self, xml_str):
        for keyword in ["<?include", "$(env", "$(var", "$(sys", "<?if", "<?else", "<?end", "<?for", "<?err", "<?war", "<?cmd"]:
            if keyword in xml_str:
                return True
        return False

    def preprocess(self):
        self.processed_file["content"] = ""
        proc_functions = [
            self.parse_include,
            self.parse_env_var,
            self.parse_sys_var,
            self.parse_cus_var,
            self.parse_if_elseif,
            self.parse_ifdef_ifndef,
            self.parse_error_warning,
            self.parse_foreach,
            self.parse_if_else_if,
            self.parse_command,
            self.format_xml_str
        ]
        xml_str = self.original_file["content"]
        xml_str = self.format_xml_str(xml_str)
        while(self.need_parse(xml_str)):
            for i in range(len(proc_functions)):
                xml_str = proc_functions[i](xml_str)
        self.processed_file["content"] = xml_str

    def save(self, file_path):
        self.processed_file["file"] = file_path
        xml_str = self.processed_file["content"]
        root = etree.fromstring(xml_str)
        try:
            with open(file_path, "w") as processed_file:
                processed_file.write("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n")
                processed_file.write(etree.tostring(root, pretty_print=True))
            return 0
        except:
            return -1

if __name__ == "__main__":
    proc = Preprocessor()
    if len(sys.argv) < 2:
        print """
    Usage:
        > python preprocessor.py <input.xml> [output.xml]
"""
        sys.exit(-1)
    input = sys.argv[1]
    if len(sys.argv) == 2:
        output = "output.xml"
    else:
        output = sys.argv[2]
    proc.load(input)
    proc.preprocess()
    proc.save(output)