class Preprocessor():
    def __init__(self):
        self.original_file = {}
        self.processed_file = {}

    def load(self, file_path):
        self.original_file["file"] = file_path
        try:
            with open(file_path, "r") as original_file:
                self.original_file["content"] = original_file.read()
            return 0
        except:
            return -1

    def preprocess(self):
        pass

    def save(self, file_path):
        self.processed_file["file"] = file_path
        try:
            with open(file_path, "w") as processed_file:
                processed_file.write(self.processed_file["content"])
            return 0
        except:
            return -1
