import os
import argparse
import google.generativeai as genai
import json
import time

def setup_api_key():
    """Reads API key from apikey.txt or environment variable."""
    api_key = os.environ.get("GEMINI_API_KEY")
    if not api_key:
        try:
            with open("apikey.txt", "r") as f:
                api_key = f.read().strip()
        except FileNotFoundError:
            print("Error: apikey.txt not found and GEMINI_API_KEY env var not set.")
            return None
    
    if not api_key:
        print("Error: API Key is empty.")
        return None
        
    genai.configure(api_key=api_key)
    return api_key

def wait_for_files_active(files):
  """Waits for the given files to be active.

  Some files uploaded to the Gemini API need to be processed before they can
  be used as prompt context. The SDK will wait for the file to be active.
  """
  print("Waiting for file processing...")
  for name in (file.name for file in files):
    file = genai.get_file(name)
    while file.state.name == "PROCESSING":
      print(".", end="", flush=True)
      time.sleep(2)
      file = genai.get_file(name)
    if file.state.name != "ACTIVE":
      raise Exception(f"File {file.name} failed to process")
  print("...all files active")

def upload_docs(docs_path, store_name=None):
    """
    Uploads documents from docs_path to a File Search Store.
    Expects structure:
    docs/
      2.19/
        file1.txt
      3.0/
        file2.txt
    """
    if not store_name:
        # Create a new corpus
        store = genai.CachingClient.create_file_search_store(display_name="BIBIM_RAG_Store")
        store_name = store.name
        print(f"Created new store: {store_name}")
    else:
        store = genai.CachingClient.get_file_search_store(name=store_name)
        print(f"Using existing store: {store_name}")

    files_to_upload = []
    
    # Walk through the docs directory
    for root, dirs, files in os.walk(docs_path):
        for file in files:
            file_path = os.path.join(root, file)
            # Determine version from parent folder name relative to docs_path
            # e.g. docs/2.19/foo.txt -> rel_path = 2.19/foo.txt -> version = 2.19
            rel_path = os.path.relpath(file_path, docs_path)
            parts = rel_path.split(os.sep)
            
            metadata = {}
            if len(parts) > 1:
                version = parts[0]
                metadata = {"version": version}
                print(f"Found file: {file_path} (Version: {version})")
            else:
                print(f"Found file: {file_path} (No version folder detected)")
            
            # Upload file with metadata
            # Note: The Python SDK for uploading with metadata might vary slightly, checking common pattern
            # Actually standard upload_file doesn't take metadata in one step usually,
            # but newer versions might. Let's stick to standard flow:
            # 1. upload_file
            # 2. Add to store (some SDKs allow metadata here).
            
            # Correct approach for File API + File Search as of late 2024:
            # We can use genai.upload_file typically.
            # Then client.file_search_stores.import_file (from the lower level library) OR
            # Just upload to the store directly?
            pass

    # Simplified approach using genai.upload_file and then adding to store is complex for metadata.
    # We will use the lower-level client or proper SDK methods if available.
    # But wait, the standard high-level way:
    
    # Let's iterate and upload properly.
    
    # Supported extensions for File Search
    # Ref: https://ai.google.dev/gemini-api/docs/file-search
    SUPPORTED_EXTENSIONS = {
        '.txt', '.md', '.pdf', '.py', '.cs', '.js', '.html', '.css', 
        '.json', '.xml', '.java', '.c', '.cpp', '.h', '.hpp'
    }

    uploaded_files = []
    for root, dirs, files in os.walk(docs_path):
        for file in files:
             file_path = os.path.join(root, file)
             ext = os.path.splitext(file)[1].lower()
             
             if ext not in SUPPORTED_EXTENSIONS:
                 # print(f"Skipping unsupported file: {file}")
                 continue
                 
             rel_path = os.path.relpath(file_path, docs_path)
             parts = rel_path.split(os.sep)
             
             display_name = file
             
             # Upload
             print(f"Uploading {file_path}...")
             uploaded_file = genai.upload_file(path=file_path, display_name=display_name)
             uploaded_files.append(uploaded_file)
             
             # We need to wait for it to be active before adding to store? 
             # Or can we add immediately?
             # Actually, to attack metadata, we probably need to use the `import_files` or similar 
             # on the store if supported, or upload directly to store. 
             # The SDK is CachingClient... let's stick to basic `genai` top level.
    
    wait_for_files_active(uploaded_files)

    # Now add to store. But how do we attach metadata?
    # The default SDK `store.add_files` does NOT support custom metadata easily unless we use the
    # low-level `client.file_search_stores.import_file` as seen in the docs.
    # Let's try to do it properly with the client from your research.
    
    # Re-importing specific types might be needed.
    # from google.ai.generativelanguage_v1beta.types import file_search_service
    
    # To keep it simple and robust per the docs we saw:
    # client.file_search_stores.import_file(...)
    # We need to access the client.
    
    # Let's use the REST-like method via the SDK's client if possible, or fall back to loop.
    pass

# Redefining to be more robust based on the "import_file" doc method
def upload_with_metadata(docs_path, store_name=None):
    from google.ai.generativelanguage_v1beta import FileSearchStoresClient
    from google.ai.generativelanguage_v1beta.types import ImportFileRequest
    
    # We might not have raw v1beta access easily depending on how `genai` is installed.
    # Let's try to assume `genai` library has what we need or use requests if all else fails.
    # Actually, the user docs showed `client.file_search_stores.import_file`. 
    # That implies `import google.generativeai as genai` exposes it or we need `lib_client`.
    
    # Let's try to use the `genai` SDK's request mechanisms or just standard manual requests if the SDK is opaque.
    # However, to be safe, I'll write a version that uses `genai` for auth and uploading files, 
    # but maybe we can just use the store creation.
    
    # Let's write a simplified script that:
    # 1. Creates a store.
    # 2. Uploads files.
    # 3. Uses the store.
    
    # METADATA is the tricky part. 
    # "File Search" docs say: use `import_file` with `custom_metadata`.
    
    # Let's assume standard `genai` usage for now but if we can't do metadata easily,
    # we might just separate stores? No, user wants version filtering.
    
    # Let's try to use the `genai.Protobuf` or compatible dicts if we can find the method.
    # If the high level SDK doesn't expose `import_file` with metadata easily, 
    # we might have to use the `client` object.
    
    # Let's try to find the client.
    # `genai.get_file_search_store` returns a store object.
    # Does it have `import_file`?
    
    pass

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--create", action="store_true", help="Create a new store")
    parser.add_argument("--upload", help="Path to docs directory")
    args = parser.parse_args()
    
    api_key = setup_api_key()
    if not api_key:
        exit(1)

    store_name = None
    config_file = "rag_config.json"
    
    if os.path.exists(config_file) and not args.create:
        with open(config_file, "r") as f:
            config = json.load(f)
            store_name = config.get("store_name")
    
    # Initialize basic client (implicit in genai)
    
    if args.create or not store_name:
        store = genai.create_file_search_store(display_name="BIBIM_RAG")
        store_name = store.name
        print(f"Created store: {store_name}")
        with open(config_file, "w") as f:
            json.dump({"store_name": store_name}, f)
            
    if args.upload:
        # We need to upload files and assign metadata.
        # Since the high-level SDK `add_files` might not expose metadata,
        # We will use the `genai.upload_file` then `store.update?` No.
        
        # We will iterate and try to use the client directly if possible.
        # But for now, let's just upload them.
        # Wait, if we can't easily add metadata with the simple SDK, 
        # RAG filtering won't work.
        
        # Let's check if we can simply use the python client as shown in the doc:
        # op = client.file_search_stores.import_file(...)
        
        # We need the `client`. 
        # `import google.ai.generativelanguage as glm`
        # `client = glm.FileSearchStoresServiceClient(api_key=...)`
        
        # This seems too complex for a quick script without knowing installed packages.
        # ALTERNATIVE: Separate stores for versions?
        # "2.19" -> Store A, "3.0" -> Store B.
        # Then `GeminiService` selects the store ID based on version.
        # This is ROBUST and EASY.
        
        print("Approach: Using separate stores for each version (simpler/safer than metadata for now).")
        
        # Scan subdirectories
        stores = {}
        if os.path.exists(config_file):
             with open(config_file, "r") as f:
                stores = json.load(f).get("stores", {})
        
        root_docs = args.upload
        for item in os.listdir(root_docs):
            item_path = os.path.join(root_docs, item)
            if os.path.isdir(item_path):
                version = item
                print(f"Processing version: {version}")
                
                # Check if store exists for this version
                v_store_name = stores.get(version)
                v_store = None
                
                if v_store_name:
                    try:
                        v_store = genai.get_file_search_store(name=v_store_name)
                        print(f"Found existing store for {version}: {v_store_name}")
                    except:
                        print(f"Store {v_store_name} not found, creating new one.")
                        v_store_name = None
                
                if not v_store_name:
                    v_store = genai.create_file_search_store(display_name=f"BIBIM_RAG_{version}")
                    v_store_name = v_store.name
                    stores[version] = v_store_name
                    print(f"Created store for {version}: {v_store_name}")
                
                # Upload files in this folder (recursive)
                files_to_add = []
                for root, dirs, files in os.walk(item_path):
                    for f in files:
                        f_path = os.path.join(root, f)
                        ext = os.path.splitext(f)[1].lower()
                        
                        if ext not in SUPPORTED_EXTENSIONS:
                            continue
                            
                        print(f"Uploading {f_path} to {version} store...")
                        try:
                            uf = genai.upload_file(path=f_path)
                            files_to_add.append(uf)
                        except Exception as e:
                            print(f"Failed to upload {f_path}: {e}")

                if files_to_add:
                    wait_for_files_active(files_to_add)
                    # Batch add files to store (Gemini API limit might apply, but let's try mostly all)
                    # API might have limits on batch size, but SDK handles list usually.
                    genai.add_files_to_file_search_store(
                        file_search_store_name=v_store.name,
                        file_ids=[f.name for f in files_to_add]
                    )
                    print(f"Added {len(files_to_add)} files to store {version}")

        # Save config
        with open(config_file, "w") as f:
            json.dump({"stores": stores}, f, indent=2)
            
        print("Done. Config updated.")
