class Preprocessor():
    def __init__(self):
        self.original_file = {}
        self.processed_file = {}

    def load(self, file_path):
        self.original_file["file"] = file_path
        try:
            with open(file_path, "r") as original_file:
                self.original_file["content"] = original_file.read().split("\n")
            return 0
        except:
            return -1

    def preprocess(self):
        for index, str in enumerate(self.original_file["content"]):
            print index, str
            #TODO: Include Files <?include?>
            #TODO: Environment Variables $(env.EnvVar)
            #TODO: System Variables $(sys.SysVar)
            #TODO: Custom Variables $(var.CusVar)
            #TODO: Conditional Statements <?if ?>, <?ifdef ?>, <?ifndef ?>, <?else?>, <?elseif ?>, <?endif?>
            #TODO: Errors and Warnings <?error?>, <?warning?>
            #TODO: Iteration Statements <?foreach?>
        self.processed_file["content"] = self.original_file["content"]

    def save(self, file_path):
        self.processed_file["file"] = file_path
        try:
            with open(file_path, "w") as processed_file:
                processed_file.write("\n".join(self.processed_file["content"]))
            return 0
        except:
            return -1
