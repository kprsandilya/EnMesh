##Inspiration
We've worked with game development before and one of the most tedious aspects is creating the game environment. Even if we have rough sketches, it takes a long time to create the necessary models in the game engine and make sure that everything looks right. We figured there was a better way.

##What it does
EnMesh is an AI-powered Unity extension that turns simple images into 3D models. It accelerates environment design and empowers game developer to prototype at rapid speed.

##How we built it
Being a Unity extension, a lot of the frontend is written in C#. The backend is written in Python using a FastAPI server to respond to incoming image conversion requests. We leverage TripoSR, a state-of-the-art model generation, to generate these models. Our backend is hosted on a virtual private server (VPS) using the provider Vultr.

##Challenges we ran into
Setting up the Python library and getting the TripoSR library working on our VPS took quite a bit of setup. We also didn't have experience with developing Unity extensions so that was also a learning curve.

##Accomplishments that we're proud of
We were able to ship a working Unity extension in 36 hours. The extension not only accurately models the provided images, it is also able to generate an environment using multiple objects, saving developers hours of work.

##What we learned
We learned the power of extension development. While less fancy than the latest AI-enabled B2B SaaS application, our product is just as relevant in today's market.

##What's next for EnMesh
We aim to add additional features, including colors, background/open-world generation, and more. We also plan to add support for other game engines such as Godot and Unreal Engine.
